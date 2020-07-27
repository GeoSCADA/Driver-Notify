using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.IO;

//In:
//C:\Users\ ... \Documents\Visual Studio 2015\Projects\Notify\Redirector\bin\Debug
//
//Install:
//c:\Windows\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe Redirector.exe

// net start RedirectorService

namespace Redirector
{
	partial class RedirectorService : ServiceBase
	{
		private System.Diagnostics.EventLog LMeventLog;
		private int eventId = 1;
		private System.Timers.Timer timer;
		private bool firstTime = true;

		internal void TestStartupAndStop(string[] args)
		{
			this.OnStart(args);
			Console.ReadLine();
			this.OnStop();
		}

		public RedirectorService()
		{
			InitializeComponent();

			// Create custom event log
			LMeventLog = new System.Diagnostics.EventLog();
			if (!System.Diagnostics.EventLog.SourceExists("RedirectorSvr"))
			{
				System.Diagnostics.EventLog.CreateEventSource(
					"RedirectorSvr", "RedirectorSvr");
			}
			LMeventLog.Source = "RedirectorSvr";
			LMeventLog.Log = ""; // Must be the same as Source, or blank
		}

		protected override void OnStart(string[] args)
		{
			LMeventLog.WriteEntry("Redirector Service Started", EventLogEntryType.Information, eventId++);
			// Set up a timer that triggers every minute.
			timer = new System.Timers.Timer();
			timer.Interval = 60000;  // 1 minute
			timer.AutoReset = true; // request rerun when ready
			timer.Elapsed += new System.Timers.ElapsedEventHandler(this.OnTimer);
			timer.Start();

			// Create output folder if it does not exist
			try
			{
				Directory.CreateDirectory(Properties.Settings.Default.WebLogPath);
			}
			catch (Exception e)
			{
				LMeventLog.WriteEntry("Error creating path: " + e.Message, EventLogEntryType.Error, eventId++);
			}

			// Run web server
			RedirectorWebServer.Start(LMeventLog, eventId);

			LMeventLog.WriteEntry("Web Server Started", EventLogEntryType.Information, eventId++);
		}

		public void OnTimer(object sender, System.Timers.ElapsedEventArgs args)
		{
			// Insert monitoring activities here.
			if (firstTime == true)
			{
				LMeventLog.WriteEntry("First scheduled operation", EventLogEntryType.Information, eventId++);
			}
			firstTime = false;
		}

		protected override void OnStop()
		{
			LMeventLog.WriteEntry("Redirector Service Stopped");
		}
	}
}
