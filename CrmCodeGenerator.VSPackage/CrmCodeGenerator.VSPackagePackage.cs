﻿#region Imports

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using WebResourceLinker;
using WebResourceLinkerExt.VSPackage.Helpers;

#endregion

namespace WebResourceLinkerExt.VSPackage
{
	/// <summary>
	///     This is the class that implements the package exposed by this assembly.
	///     The minimum requirement for a class to be considered a valid package for Visual Studio
	///     is to implement the IVsPackage interface and register itself with the shell.
	///     This package uses the helper classes defined inside the Managed Package Framework (MPF)
	///     to do it: it derives from the Package class that provides the implementation of the
	///     IVsPackage interface and uses the registration attributes defined in the framework to
	///     register itself and its components with the shell.
	/// </summary>
	// This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
	// a package.
	[PackageRegistration(UseManagedResourcesOnly = true)]
	// This attribute is used to register the information needed to show this package
	// in the Help/About dialog of Visual Studio.
	[InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
	//this causes the class to load when VS starts [ProvideAutoLoad("ADFC4E64-0397-11D1-9F4E-00A0C911004F")]
	[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasSingleProject_string)]
	[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasMultipleProjects_string)]
	// This attribute is needed to let the shell know that this package exposes some menus.
	[ProvideMenuResource("Menus.ctmenu", 1)]
	[Guid(GuidList.guidWebResLinkExt_VSPackagePkgString)]
	public sealed class WebResourceLinkerExt_VSPackagePackage : Package,
		IVsSolutionEvents3
	{
		/// <summary>
		///     Default constructor of the package.
		///     Inside this method you can place any initialization code that does not require
		///     any Visual Studio service because at this point the package object is created but
		///     not sited yet inside Visual Studio environment. The place to do all the other
		///     initialization is the Initialize method.
		/// </summary>
		public WebResourceLinkerExt_VSPackagePackage()
		{
			Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", ToString()));
		}


		/////////////////////////////////////////////////////////////////////////////
		// Overridden Package Implementation

		#region Package Members

		/// <summary>
		///     Initialization of the package; this method is called right after the package is sited, so this is the place
		///     where you can put all the initialization code that rely on services provided by VisualStudio.
		/// </summary>
		protected override void Initialize()
		{
			AssemblyHelpers.RedirectAssembly("Microsoft.Xrm.Sdk", new Version("9.0.0.0"), "31bf3856ad364e35");
			////AssemblyHelpers.RedirectAssembly("Microsoft.IdentityModel.Clients.ActiveDirectory",
			////	new Version("2.22.0.0"), "31bf3856ad364e35");

			Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", ToString()));
			base.Initialize();

			// Add our command handlers for menu (commands must exist in the .vsct file)
			var mcs = GetService(typeof (IMenuCommandService)) as OleMenuCommandService;

			if (null != mcs)
			{
				//var codeCmd = new CommandID(GuidList.guidWebResLinkExt_VSPackageCmdSet,
				//	(int) PkgCmdIDList.cmdidCode);
				//var codeItem = new MenuCommand(CodeCallback, codeCmd);
				//mcs.AddCommand(codeItem);

				var fileCmd = new CommandID(GuidList.guidWebResLinkExt_VSPackageCmdSet,
					(int) PkgCmdIDList.cmdidFile);
				var fileItem = new MenuCommand(CodeCallback, fileCmd);
				mcs.AddCommand(fileItem);

				var relinkCmd = new CommandID(GuidList.guidWebResLinkExt_VSPackageCmdSet,
					(int) PkgCmdIDList.cmdidFileRelink);
				var relinkItem = new MenuCommand(RelinkCallback, relinkCmd);
				mcs.AddCommand(relinkItem);
			}

			AdviseSolutionEvents();
		}

		protected override void Dispose(bool disposing)
		{
			UnadviseSolutionEvents();

			base.Dispose(disposing);
		}

		private IVsSolution solution = null;
		private uint _handleCookie;

		private void AdviseSolutionEvents()
		{
			UnadviseSolutionEvents();

			solution = GetService(typeof (SVsSolution)) as IVsSolution;

			if (solution != null)
			{
				solution.AdviseSolutionEvents(this, out _handleCookie);
			}
		}

		private void UnadviseSolutionEvents()
		{
			if (solution != null)
			{
				if (_handleCookie != uint.MaxValue)
				{
					solution.UnadviseSolutionEvents(_handleCookie);
					_handleCookie = uint.MaxValue;
				}

				solution = null;
			}
		}

		#endregion

		/// <summary>
		///     This function is the callback used to execute a command when the a menu item is clicked.
		///     See the Initialize method to see how the menu item is associated to this function using
		///     the OleMenuCommandService service and the MenuCommand class.
		/// </summary>
		private void CodeCallback(object sender, EventArgs args)
		{
			HandleCodeWindowCommand(false);
		}

		private void RelinkCallback(object sender, EventArgs args)
		{
			HandleCodeWindowCommand(true);
		}

		private void HandleCodeWindowCommand(bool relinking)
		{
			try
			{
				Status.Update(">>>>> Starting new session <<<<<");

				try
				{
					var _applicationObject = GetService(typeof (SDTE)) as DTE2;

					const string linkerDataPath = "WebResLink.dat"; // this is our mapping file, it gets stored in the project
					var solutionPath = Path.GetDirectoryName(_applicationObject.Solution.FullName);

					var selectedFiles = new List<SelectedFile>();

					// this handles a right click from a codewindow (eg: from the c#/js editor window)
					// todo: need to figure out how to support other editors like the css editor
					if (_applicationObject.ActiveWindow?.Project != null)
					{
						selectedFiles.Add(new SelectedFile
						                  {
							                  FilePath = _applicationObject.ActiveWindow.Document.FullName,
							                  FriendlyFilePath =
								                  GetFriendlyPath(solutionPath, _applicationObject.ActiveWindow.Document.FullName)
						                  });
					}
					else if (_applicationObject.SelectedItems != null)
					{
						var items = _applicationObject.SelectedItems.Cast<SelectedItem>().ToList();

						if (relinking && items.Count > 1) // todo: add support for multiple relinks later
						{
							MessageBox.Show("You can only re-link 1 web resource at a time", "ERROR!", MessageBoxButtons.OK,
								MessageBoxIcon.Error);
							return;
						}

						foreach (var item in items)
						{
							// vs has crazy logic, need to figure out the fullpath to the file so that we can correctly map files. just incase files within nested folders have the same name
							var path = item.ProjectItem.Properties.Item("FullPath").Value.ToString();
							selectedFiles.Add(new SelectedFile {FilePath = path, FriendlyFilePath = GetFriendlyPath(solutionPath, path)});
						}
					}

					// sanity checks to make sure we dont crash
					if (!string.IsNullOrEmpty(linkerDataPath) && selectedFiles.Count > 0)
					{
						new Controller(this).TryLinkOrPublish(linkerDataPath, selectedFiles, relinking);
					}
				}
				catch (Exception ex)
				{
					MessageBox.Show(ex.Message, "ERROR!", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			catch (UserException e)
			{
				VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, e.Message, "Error", OLEMSGICON.OLEMSGICON_WARNING,
					OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
			}
			catch (Exception e)
			{
				var error1 = "[ERROR] " + e.Message
				             + (e.InnerException != null ? "\n" + "[ERROR] " + e.InnerException.Message : "");
				Status.Update(error1);
				Status.Update(e.StackTrace);
				Status.Update("Unable to register assembly, see error above.");
				var error2 = e.Message + "\n" + e.StackTrace;
				MessageBox.Show(error2, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		public void PublishingComplete()
		{
		}

		private static string GetFriendlyPath(string solutionPath, string filePath)
		{
			return filePath.Replace(solutionPath, "");
				// this is for the mappings xml, we'll take away all drive specifics so that if multiple devs are using the solution on different paths we still base it from where the solution dir started
		}

		private OutputWindowPane _outputWindow;

		public void WriteToOutputWindow(string format, params object[] args)
		{
			var applicationObject = GetService(typeof (SDTE)) as DTE2;

			if (_outputWindow == null)
			{
				if (applicationObject != null) {
					var window = applicationObject.Windows.Item("{34E76E81-EE4A-11D0-AE2E-00A0C90FFFC3}");
					var outputWindow = (OutputWindow) window.Object;
					_outputWindow = outputWindow.OutputWindowPanes.Add("Web Resource Linker");
				}
			}
			if (_outputWindow == null)
			{
				return;
			}

			_outputWindow.OutputString(string.Format(format, args));
			_outputWindow.OutputString(Environment.NewLine);
		}

		#region SolutionEvents

		public int OnAfterCloseSolution(object pUnkReserved)
		{
			return VSConstants.S_OK;
		}

		public int OnAfterClosingChildren(IVsHierarchy pHierarchy)
		{
			return VSConstants.S_OK;
		}

		public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
		{
			return VSConstants.S_OK;
		}

		public int OnAfterMergeSolution(object pUnkReserved)
		{
			return VSConstants.S_OK;
		}

		public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
		{
			return VSConstants.S_OK;
		}

		public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
		{
			return VSConstants.S_OK;
		}

		public int OnAfterOpeningChildren(IVsHierarchy pHierarchy)
		{
			return VSConstants.S_OK;
		}

		public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
		{
			return VSConstants.S_OK;
		}

		public int OnBeforeCloseSolution(object pUnkReserved)
		{
			return VSConstants.S_OK;
		}

		public int OnBeforeClosingChildren(IVsHierarchy pHierarchy)
		{
			return VSConstants.S_OK;
		}

		public int OnBeforeOpeningChildren(IVsHierarchy pHierarchy)
		{
			return VSConstants.S_OK;
		}

		public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
		{
			return VSConstants.S_OK;
		}

		public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
		{
			return VSConstants.S_OK;
		}

		public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
		{
			return VSConstants.S_OK;
		}

		public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
		{
			return VSConstants.S_OK;
		}

		#endregion
	}
	public class AssemblyHelpers
	{
		// credit: http://blog.slaks.net/2013-12-25/redirecting-assembly-loads-at-runtime/
		public static void RedirectAssembly(string shortName, Version targetVersion, string publicKeyToken)
		{
			Assembly Handler(object sender, ResolveEventArgs args)
			{
				// Use latest strong name & version when trying to load SDK assemblies
				var requestedAssembly = new AssemblyName(args.Name);

				if (requestedAssembly.Name != shortName && !requestedAssembly.FullName.Contains(shortName + ","))
				{
					return null;
				}

				Debug.WriteLine("Redirecting assembly load of " + args.Name + ",\tloaded by " + (args.RequestingAssembly == null ? "(unknown)" : args.RequestingAssembly.FullName));

				requestedAssembly.Version = targetVersion;
				requestedAssembly.SetPublicKeyToken(new AssemblyName("x, PublicKeyToken=" + publicKeyToken).GetPublicKeyToken());
				requestedAssembly.CultureInfo = CultureInfo.InvariantCulture;

				AppDomain.CurrentDomain.AssemblyResolve -= Handler;

				return Assembly.Load(requestedAssembly);
			}

			AppDomain.CurrentDomain.AssemblyResolve += Handler;
		}
	}
}
