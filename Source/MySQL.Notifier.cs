﻿// Copyright (c) 2006-2008 MySQL AB, 2008-2009 Sun Microsystems, Inc.
//
// MySQL Connector/NET is licensed under the terms of the GPLv2
// <http://www.gnu.org/licenses/old-licenses/gpl-2.0.html>, like most 
// MySQL Connectors. There are special exceptions to the terms and 
// conditions of the GPLv2 as it is applied to this software, see the 
// FLOSS License Exception
// <http://www.mysql.com/about/legal/licensing/foss-exception.html>.
//
// This program is free software; you can redistribute it and/or modify 
// it under the terms of the GNU General Public License as published 
// by the Free Software Foundation; version 2 of the License.
//
// This program is distributed in the hope that it will be useful, but 
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY 
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License 
// for more details.
//
// You should have received a copy of the GNU General Public License along 
// with this program; if not, write to the Free Software Foundation, Inc., 
// 51 Franklin St, Fifth Floor, Boston, MA 02110-1301  USA

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.ServiceProcess;
using System.Reflection;
using System.Drawing;
using System.Diagnostics;
using System.ComponentModel;
using System.Linq;
using System.Management;
using MySql.Notifier.Properties;
using MySQL.Utility;
using System.IO;
using System.Configuration;



namespace MySql.Notifier
{
  class Notifier
  {
    private System.ComponentModel.IContainer components;
    private NotifyIcon notifyIcon;
    private MySQLServicesList mySQLServicesList { get; set; }
    
    private ManagementEventWatcher watcher;

    private ToolStripMenuItem installAvailablelUpdates;
    private ToolStripMenuItem ignoreAvailableUpdate;
    private ToolStripSeparator separator;

    public Notifier()
    {      
     
      components = new System.ComponentModel.Container();
      notifyIcon = new NotifyIcon(components)
                    {
                      ContextMenuStrip = new ContextMenuStrip(),
                      Icon = Icon.FromHandle(GetIconForNotifier().GetHicon()),
                      Visible = true
                    };

      notifyIcon.MouseClick += notifyIcon_MouseClick;
      notifyIcon.ContextMenuStrip.Opening += new CancelEventHandler(ContextMenuStrip_Opening);
      notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
      notifyIcon.BalloonTipTitle = Properties.Resources.BalloonTitleTextServiceStatus;

      // Setup our service list
      mySQLServicesList = new MySQLServicesList();
      mySQLServicesList.ServiceStatusChanged += mySQLServicesList_ServiceStatusChanged;
      mySQLServicesList.ServiceListChanged += new MySQLServicesList.ServiceListChangedHandler(mySQLServicesList_ServiceListChanged);

      // create scheduled task to check for updates 
      // when first run
      if (Settings.Default.FirstRun && Settings.Default.AutoCheckForUpdates && Settings.Default.CheckForUpdatesFrequency > 0)
      {
        if (!String.IsNullOrEmpty(Utility.GetInstallLocation("MySQL Notifier")))
        {
          Utility.CreateScheduledTask("MySQLNotifierTask", @"""" + Utility.GetInstallLocation("MySQL Notifier") + @"MySql.Notifier.exe --c""",
            Settings.Default.CheckForUpdatesFrequency, false);
        }
      }

      // loads all the services from our settings file and sets up their menus
      mySQLServicesList.LoadFromSettings();
      AddStaticMenuItems();
      SetNotifyIconToolTip();
      
      if (Settings.Default.UpdateCheck == (int)SoftwareUpdateStaus.HasUpdates)
      {        
        separator =  new ToolStripSeparator();

        installAvailablelUpdates = new ToolStripMenuItem("Install available updates...");
        installAvailablelUpdates.Click += new EventHandler(InstallAvailablelUpdates_Click);

        ignoreAvailableUpdate = new ToolStripMenuItem("Ignore this update");
        ignoreAvailableUpdate.Click += new EventHandler(IgnoreAvailableUpdateItem_Click);

        notifyIcon.ContextMenuStrip.Items.Add(separator);
        notifyIcon.ContextMenuStrip.Items.Add(installAvailablelUpdates);
        notifyIcon.ContextMenuStrip.Items.Add(ignoreAvailableUpdate);
      }

      StartWatchingSettingsFile();

      // listener for events
      var managementScope = new ManagementScope(@"root\cimv2");
      managementScope.Connect();

      // WqlEventQuery query = new WqlEventQuery("__InstanceModificationEvent", new TimeSpan(0, 0, 1), "TargetInstance isa \"Win32_Service\" AND ( TargetInstance.Name LIKE \"%MYSQL%\" OR TargetInstance.PathName LIKE \"%MYSQL%\" ) ");
      WqlEventQuery query = new WqlEventQuery("__InstanceModificationEvent", new TimeSpan(0, 0, 1), "TargetInstance isa \"Win32_Service\"");
      watcher = new ManagementEventWatcher(managementScope, query);
      watcher.EventArrived += new EventArrivedEventHandler(watcher_EventArrived);
      watcher.Start();    
        
    }

    /// <summary>
    /// Generic routine to help with showing tooltips
    /// </summary>
    void ShowTooltip(bool error, string title, string text, int delay)
    {
      notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
      notifyIcon.BalloonTipTitle = title;
      notifyIcon.BalloonTipText = text; 
      notifyIcon.ShowBalloonTip(delay);
    }

    private void StartWatchingSettingsFile()
    {
      Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
      FileSystemWatcher watcher = new FileSystemWatcher();
      watcher.Path = Path.GetDirectoryName(config.FilePath);
      watcher.Filter = Path.GetFileName(config.FilePath);
      watcher.NotifyFilter = NotifyFilters.LastWrite;
      watcher.Changed += new FileSystemEventHandler(settingsFile_Changed);
      watcher.EnableRaisingEvents = true;
    }

    void settingsFile_Changed(object sender, FileSystemEventArgs e)
    {
      Settings.Default.Reload();

      // if we have already notified our user then noting more to do
      if((Settings.Default.UpdateCheck & (int)SoftwareUpdateStaus.Notified) != 0) return;

      // let them know we are checking for updates
      if ((Settings.Default.UpdateCheck & (int)SoftwareUpdateStaus.Checking) != 0)
        ShowTooltip(false, Resources.SoftwareUpdate, Resources.CheckingForUpdates, 1500);

      else if ((Settings.Default.UpdateCheck & (int)SoftwareUpdateStaus.HasUpdates) != 0)
        ShowTooltip(false, Resources.SoftwareUpdate, Resources.HasUpdatesLaunchInstaller, 1500);

      // set that we have notified our user
      Settings.Default.UpdateCheck |= (int)SoftwareUpdateStaus.Notified;
      Settings.Default.Save();
    }

    void notifyIcon_MouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
        {
            MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            mi.Invoke(notifyIcon, null);
        }
    }    

    void ContextMenuStrip_Opening(object sender, CancelEventArgs e)
    {
      foreach (MySQLService service in mySQLServicesList.Services)
        service.MenuGroup.Update();
    }

    /// <summary>
    /// Adds the static menu items such as Options, Exit, About..
    /// </summary>
    private void AddStaticMenuItems()
    {

      ToolStripMenuItem manageServices = new ToolStripMenuItem("Manage Services...");    
      manageServices.Click += new EventHandler(manageServicesDialogItem_Click);
      manageServices.Image = Resources.ManageServicesIcon;

      ToolStripMenuItem launchInstaller = new ToolStripMenuItem("Launch Installer");
      bool installerInstalled = MySqlInstaller.IsInstalled;
      launchInstaller.Click += new EventHandler(launchInstallerItem_Click);
      launchInstaller.Image = Resources.StartInstallerIcon;
      launchInstaller.Enabled = installerInstalled;

      ToolStripMenuItem checkForUpdates = new ToolStripMenuItem("Check for updates");
      checkForUpdates.Click += new EventHandler(checkUpdatesItem_Click);
      checkForUpdates.Enabled = !String.IsNullOrEmpty(MySqlInstaller.GetInstallerPath()) && MySqlInstaller.GetInstallerVersion().Contains("1.1");
      checkForUpdates.Image = Resources.CheckForUpdatesIcon;

      ToolStripMenuItem actionsMenu = new ToolStripMenuItem("Actions", null, manageServices, launchInstaller, checkForUpdates);

      ToolStripMenuItem optionsMenu = new ToolStripMenuItem("Options...");
      optionsMenu.Click += new EventHandler(optionsItem_Click);

      ToolStripMenuItem aboutMenu = new ToolStripMenuItem("About...");
      aboutMenu.Click += new EventHandler(aboutMenu_Click);

      ToolStripMenuItem exitMenu = new ToolStripMenuItem("Close MySQL Notifier");
      exitMenu.Click += new EventHandler(exitItem_Click);

      actionsMenu.DropDownItems.Add(new ToolStripSeparator());
      actionsMenu.DropDownItems.Add(optionsMenu);
      actionsMenu.DropDownItems.Add(aboutMenu);
      actionsMenu.DropDownItems.Add(exitMenu);

      notifyIcon.ContextMenuStrip.Items.Add(actionsMenu);
    }

    private void ServiceListChanged(MySQLService service, ServiceListChangeType changeType)
    {
      if (changeType == ServiceListChangeType.Remove)
      {
        service.MenuGroup.RemoveFromContextMenu(notifyIcon.ContextMenuStrip);
        return;
      }

      // the rest of this is for additions
      service.MenuGroup.AddToContextMenu(notifyIcon.ContextMenuStrip);
      service.StatusChangeError += new MySQLService.StatusChangeErrorHandler(service_StatusChangeError);
      if (changeType == ServiceListChangeType.AutoAdd && Settings.Default.NotifyOfAutoServiceAddition)
      {
        notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        notifyIcon.BalloonTipTitle = Resources.BalloonTitleTextServiceList;
        notifyIcon.BalloonTipText = String.Format(Resources.BalloonTextServiceList, service.ServiceName);
        notifyIcon.ShowBalloonTip(1500);
      }
    }

    void service_StatusChangeError(object sender, Exception ex)
    {
      MySQLService service = (MySQLService)sender;
      notifyIcon.BalloonTipIcon = ToolTipIcon.Error;
      notifyIcon.BalloonTipTitle = Resources.BalloonTitleFailedStatusChange;
      notifyIcon.BalloonTipText = String.Format(Resources.BalloonTextFailedStatusChange, service.ServiceName, ex.Message);
      notifyIcon.ShowBalloonTip(1500);
    }

    void mySQLServicesList_ServiceListChanged(object sender, MySQLService service, ServiceListChangeType changeType)
    {
      ServiceListChanged(service, changeType);
    }

    /// <summary>
    /// Notifies that the Notifier wants to quit
    /// </summary>
    public event EventHandler Exit;

    /// <summary>
    /// Invokes the Exit event
    /// </summary>
    /// <param name="e">Event arguments</param>
    protected virtual void OnExit(EventArgs e)
    {
      notifyIcon.Visible = false;

      watcher.Stop();

      if (this.Exit != null)
        Exit(this, e);
    }

    private void mySQLServicesList_ServiceStatusChanged(object sender, ServiceStatus args)
    {
      if (!Settings.Default.NotifyOfStatusChange) return;

      MySQLService service = mySQLServicesList.GetServiceByName(args.ServiceName);      

      if (!service.NotifyOnStatusChange) return;

      if (service.UpdateTrayIconOnStatusChange) notifyIcon.Icon = Icon.FromHandle(GetIconForNotifier().GetHicon());

      notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
      notifyIcon.BalloonTipTitle = Resources.BalloonTitleTextServiceStatus;
      notifyIcon.BalloonTipText = String.Format(Resources.BalloonTextServiceStatus,
                                                      args.ServiceName,
                                                      args.PreviousStatus.ToString(),
                                                      args.CurrentStatus.ToString());
      notifyIcon.ShowBalloonTip(1500);
    }

   
    private void manageServicesDialogItem_Click(object sender, EventArgs e)
    {
      ManageServicesDlg dlg = new ManageServicesDlg(mySQLServicesList);
      dlg.ShowDialog();    
      //update icon 
      notifyIcon.Icon = Icon.FromHandle(GetIconForNotifier().GetHicon());
    }


    private void launchInstallerItem_Click(object sender, EventArgs e)
    {
      if (!String.IsNullOrEmpty(MySqlInstaller.GetInstallerPath()))
      {              
        string path = @MySqlInstaller.GetInstallerPath();
        Process proc = new Process();
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = @String.Format(@"{0}\MySQLInstaller.exe", @path);
        Process.Start(startInfo);            
      }      
    }

    private void checkUpdatesItem_Click(object sender, EventArgs e)
    {
      if (!String.IsNullOrEmpty(MySqlInstaller.GetInstallerPath()))
      {
        string path = @MySqlInstaller.GetInstallerPath();
        Process proc = new Process();
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = @String.Format(@"{0}\MySQLInstaller.exe", @path);
        startInfo.Arguments = "-checkforupdates";
        Process.Start(startInfo);
      }      
    }

    private void aboutMenu_Click(object sender, EventArgs e)
    {
      AboutDialog dlg = new AboutDialog();
      dlg.ShowDialog();
    }


    private void optionsItem_Click(object sender, EventArgs e)
    {
      OptionsDialog dlg = new OptionsDialog();
      dlg.ShowDialog();      
    }


    private void InstallAvailablelUpdates_Click(object sender, EventArgs e)
    {
      //TODO  InstallAvailablelUpdates_Click
    
    }

    private void IgnoreAvailableUpdateItem_Click(object sender, EventArgs e)
    {
      if (MessageBox.Show("This action will completely ignore the available software updates. Would you like to continue?", "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
      {
        Properties.Settings.Default.UpdateCheck = 0;
        Properties.Settings.Default.Save();        
        
        // update UI
        notifyIcon.Icon = Icon.FromHandle(GetIconForNotifier().GetHicon());
        notifyIcon.ContextMenuStrip.Items.Remove(separator);
        notifyIcon.ContextMenuStrip.Items.Remove(installAvailablelUpdates);
        notifyIcon.ContextMenuStrip.Items.Remove(ignoreAvailableUpdate);
      }       
    }

    /// <summary>
    /// When the exit menu item is clicked, make a call to terminate the ApplicationContext.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void exitItem_Click(object sender, EventArgs e)
    {
      OnExit(EventArgs.Empty);
    }

    /// <summary>
    /// Sets the text displayed in the notify icon's tooltip
    /// </summary>
    public void SetNotifyIconToolTip()
    {
      int MAX_TOOLTIP_LENGHT = 63; // framework constraint for notify icons

      string toolTipText = string.Format("{0} ({1})\n{2}.",
                                         Properties.Resources.AppName,
                                         Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                                         String.Format(Properties.Resources.ToolTipText, mySQLServicesList.Services.Count));
      notifyIcon.Text = (toolTipText.Length >= MAX_TOOLTIP_LENGHT ? toolTipText.Substring(0, MAX_TOOLTIP_LENGHT - 3) + "..." : toolTipText);
    }

    public void watcher_EventArrived(object sender, EventArrivedEventArgs args)
    {
      var e = args.NewEvent;
      ManagementBaseObject o = ((ManagementBaseObject)e["TargetInstance"]);
      if (o == null) return;

      string state = o["State"].ToString().Trim();
      string serviceName = o["DisplayName"].ToString().Trim();
      string path = o["PathName"].ToString();

      if (state.Contains("Pending")) return;

      Control c = notifyIcon.ContextMenuStrip;
      if (c.InvokeRequired)
        c.Invoke((MethodInvoker)delegate
        {
          mySQLServicesList.SetServiceStatus(serviceName, path, state);
          SetNotifyIconToolTip();
          if (mySQLServicesList.GetServiceByName(serviceName) != null)
          {
            if (mySQLServicesList.GetServiceByName(serviceName).UpdateTrayIconOnStatusChange)
             notifyIcon.Icon = Icon.FromHandle(GetIconForNotifier().GetHicon());            
          }
        });

      else
      {
        mySQLServicesList.SetServiceStatus(serviceName, path, state);
        SetNotifyIconToolTip();
        if (mySQLServicesList.GetServiceByName(serviceName) != null)
        {
          if (mySQLServicesList.GetServiceByName(serviceName).UpdateTrayIconOnStatusChange)
            notifyIcon.Icon = Icon.FromHandle(GetIconForNotifier().GetHicon());
        }
      }
    }


    private Bitmap GetIconForNotifier()
    {
      
      if (Settings.Default.ServiceList == null)
        return Settings.Default.UpdateCheck == (int)SoftwareUpdateStaus.HasUpdates ? 
               Properties.Resources.NotifierIconAlert :
               Properties.Resources.NotifierIcon;
     
      var updateTrayIconServices = Settings.Default.ServiceList.Where(t => t.UpdateTrayIconOnStatusChange);

      if (updateTrayIconServices != null)
      {
        if (updateTrayIconServices.Where(t => t.Status == ServiceControllerStatus.Stopped).Count() > 0)
          return Settings.Default.UpdateCheck == (int)SoftwareUpdateStaus.HasUpdates ?
                  Properties.Resources.NotifierIconStoppedAlert :
                  Properties.Resources.NotifierIconStopped;

        if (updateTrayIconServices.Where(t => t.Status == ServiceControllerStatus.StartPending).Count() > 0)
          return Settings.Default.UpdateCheck == (int)SoftwareUpdateStaus.HasUpdates ?
                  Properties.Resources.NotifierIconStartingAlert :
                  Properties.Resources.NotifierIconStarting;


        if (updateTrayIconServices.Where(t => t.Status == ServiceControllerStatus.Running).Count() > 0)
          return Settings.Default.UpdateCheck == (int)SoftwareUpdateStaus.HasUpdates ?
                  Properties.Resources.NotifierIconRunningAlert:
                  Properties.Resources.NotifierIconRunning;      
      }

      return Properties.Resources.NotifierIcon;      
    }
  }  

  public enum SoftwareUpdateStaus : int
  {
    Checking = 1,
    HasUpdates = 2,
    Notified = 4
  }
}