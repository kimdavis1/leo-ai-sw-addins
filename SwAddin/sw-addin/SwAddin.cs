using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using SolidWorksTools.File;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using sw_addin;
using sw_addin.Logs;

namespace SwLeoAIAddin
{
	[Guid("F46B2D04-9B8B-48F8-9F68-C1C022D4991C"), ComVisible(true)]
	[SwAddin(
			Description = "Your AI engineering design copilot",
			Title = "Leo AI copilot",
			LoadAtStartup = true
			)]
	public class SwAddin : ISwAddin
	{
		#region Local Variables
		ISldWorks iSwApp = null;
		ICommandManager iCmdMgr = null;
		ICommandGroup cmdGroup;
		private CommandManager swCmdMgr;
		int addinID = 2;
		BitmapHandler iBmp;
		int registerID;

		public const int mainCmdGroupID = 6;
		public const int mainItemID1 = 0;
		public const int mainItemID2 = 1;
		public const int mainItemID3 = 2;
		private int _cmdGroupID = 5;

		string[] mainIcons = new string[2];
		string[] icons = new string[2];

		public SwHelper SolidWorksHelper { get; set; }

		#region Event Handler Variables
		Hashtable openDocs = new Hashtable();
		SldWorks SwEventPtr = null;
		#endregion

		#region Property Manager Variables
		public UserPMPage ppage = null;
		#endregion


		// Public Properties
		public ISldWorks SwApp
		{
			get { return iSwApp; }
		}
		public ICommandManager CmdMgr
		{
			get { return iCmdMgr; }
		}

		public Hashtable OpenDocs
		{
			get { return openDocs; }
		}

		#endregion

		#region SolidWorks Registration
		[ComRegisterFunctionAttribute]
		public static void RegisterFunction(Type t)
		{
			#region Get Custom Attribute: SwAddinAttribute
			SwAddinAttribute SWattr = null;
			Type type = typeof(SwAddin);

			foreach (System.Attribute attr in type.GetCustomAttributes(false))
			{
				if (attr is SwAddinAttribute)
				{
					SWattr = attr as SwAddinAttribute;
					break;
				}
			}

			#endregion

			try
			{
				Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
				Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

				string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
				Microsoft.Win32.RegistryKey addinkey = hklm.CreateSubKey(keyname);
				addinkey.SetValue(null, 0);

				addinkey.SetValue("Description", SWattr.Description);
				addinkey.SetValue("Title", SWattr.Title);

				keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
				addinkey = hkcu.CreateSubKey(keyname);
				addinkey.SetValue(null, Convert.ToInt32(SWattr.LoadAtStartup), Microsoft.Win32.RegistryValueKind.DWord);
			}
			catch (NullReferenceException nl)
			{
				Console.WriteLine("There was a problem registering this dll: SWattr is null. \n\"" + nl.Message + "\"");
				MessageBox.Show("There was a problem registering this dll: SWattr is null.\n\"" + nl.Message + "\"");
			}

			catch (System.Exception e)
			{
				Console.WriteLine(e.Message);

				MessageBox.Show("There was a problem registering the function: \n\"" + e.Message + "\"");
			}
		}

		[ComUnregisterFunctionAttribute]
		public static void UnregisterFunction(Type t)
		{
			try
			{
				Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
				Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

				string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
				hklm.DeleteSubKey(keyname);

				keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
				hkcu.DeleteSubKey(keyname);
			}
			catch (NullReferenceException nl)
			{
				Console.WriteLine("There was a problem unregistering this dll: " + nl.Message);
				MessageBox.Show("There was a problem unregistering this dll: \n\"" + nl.Message + "\"");
			}
			catch (System.Exception e)
			{
				Console.WriteLine("There was a problem unregistering this dll: " + e.Message);
				MessageBox.Show("There was a problem unregistering this dll: \n\"" + e.Message + "\"");
			}
		}

		#endregion

		#region ISwAddin Implementation
		private ImageList selectionImageList;

		private string AssemblyLocation;

		public SwAddin()
		{
			AssemblyLocation = Path.GetDirectoryName(Assembly.GetAssembly(GetType()).Location);
			string iconsFolderLoc = AssemblyLocation + @"\Icons";

			LogFileWriter.Write($"Leo AI : SW Addin Icons : {iconsFolderLoc}.");
			selectionImageList = new ImageList();
			selectionImageList.ImageSize = new Size(16, 16);
			selectionImageList.Images.Add(Image.FromFile(iconsFolderLoc + @"\icon16x16.bmp"));
		
		}

		private const int SURFACE_MENU_ID = 1;
		private IModelDoc2 activeModelDoc = null;

		private LeoWebServerListener localWebServer;
		public bool ConnectToSW(object ThisSW, int cookie)
		{
			iSwApp = (ISldWorks)ThisSW;
			addinID = cookie;

			//Setup callbacks
			iSwApp.SetAddinCallbackInfo(0, this, addinID);

			#region Setup the Command Manager
			iCmdMgr = iSwApp.GetCommandManager(cookie);
			//swCmdMgr = iSwApp.GetCommandManager(cookie) as CommandManager;
			AddCommandMgr();
			#endregion

			#region Setup the Event Handlers
			SwEventPtr = (SldWorks)iSwApp;
			openDocs = new Hashtable();
			AttachEventHandlers();
			#endregion

			//start the listener to recieve data from leo
			LogFileWriter.Write($"Leo AI : Web server Listener Start :");
			localWebServer = new LeoWebServerListener(SolidWorksHelper);
			localWebServer.StartListener();

			return true;
		}

		public bool DisconnectFromSW()
		{
			RemoveCommandMgr();
			DetachEventHandlers();

			Marshal.ReleaseComObject(iCmdMgr);
			iCmdMgr = null;
			Marshal.ReleaseComObject(iSwApp);
			iSwApp = null;
			//The addin _must_ call GC.Collect() here in order to retrieve all managed code pointers 
			GC.Collect();
			GC.WaitForPendingFinalizers();

			GC.Collect();
			GC.WaitForPendingFinalizers();

			// Clean up the server when the add-in is unloaded
			if (localWebServer != null)
			{
				LogFileWriter.Write($"Leo AI : Web server Listener Stop .");
				localWebServer.StopListener();
			}

			return true;
		}
		#endregion

		#region UI Methods
		public void AddCommandMgr()
		{
			try
			{

				LogFileWriter.Write($"Leo AI Solidworks Addin Load Start: ");
				ICommandGroup cmdGroup;
				if (iBmp == null)
					iBmp = new BitmapHandler();
				//Initialize the helper class to support Solidworks  actions
				SolidWorksHelper = new SwHelper(iSwApp);
				int[] cmdIndex = new int[2]; //, cmdIndex1;
				string Title = "Leo AI", ToolTip = "Your AI engineering design copilot";
				string iconsFolderLoc = AssemblyLocation + @"\Icons";

				//Addin exists in Part and assembly environment only..
				int[] docTypes = new int[] { (int)swDocumentTypes_e.swDocPART , (int)swDocumentTypes_e.swDocASSEMBLY};

			
				bool ignorePrevious = false;

				object registryIDs;
				//get the ID information stored in the registry
				bool getDataResult = iCmdMgr.GetGroupDataFromRegistry(mainCmdGroupID, out registryIDs);

				int[] knownIDs = new int[2] { mainItemID1, mainItemID2 };

				if (getDataResult)
				{
					if (!CompareIDs((int[])registryIDs, knownIDs)) //if the IDs don't match, reset the commandGroup
					{
						ignorePrevious = true;
					}
				}

				int errors = 0;
				cmdGroup = iCmdMgr.CreateCommandGroup2(mainCmdGroupID, Title, ToolTip, "", -1, ignorePrevious, ref errors);

				cmdGroup.IconList = new string[] { iconsFolderLoc + @"\icon16x16.bmp", iconsFolderLoc + @"\icon32x32.bmp" };
				int menuToolbarOption = (int)(swCommandItemType_e.swToolbarItem | swCommandItemType_e.swMenuItem);
				cmdIndex[0] = cmdGroup.AddCommandItem2(
					 "Turn Leo on",
					 -1,
					 "Turn Leo on",
					 "Turn Leo On",
					 0,
					 "LaunchLeoApp",
					 "",
					 mainItemID1,
					 menuToolbarOption
				);

				cmdIndex[1] = cmdGroup.AddCommandItem2(
				 "Find Component",
				 -1,
				 "Geometry based component search",
				 "Find Component",
				 0,
				 "SearchPart",
				 "",
				 mainItemID1,
				 menuToolbarOption
			);

				cmdGroup.HasToolbar = true;
				cmdGroup.HasMenu = true;
				cmdGroup.Activate();


				bool bResult;

				foreach (int type in docTypes)
				{
					CommandTab cmdTab;

					cmdTab = iCmdMgr.GetCommandTab(type, Title);

					if (cmdTab != null & !getDataResult | ignorePrevious)//if tab exists, but we have ignored the registry info (or changed command group ID), re-create the tab.  Otherwise the ids won't matchup and the tab will be blank
					{
						bool res = iCmdMgr.RemoveCommandTab(cmdTab);
						cmdTab = null;
					}

					//if cmdTab is null, must be first load (possibly after reset), add the commands to the tabs
					if (cmdTab == null)
					{
						cmdTab = iCmdMgr.AddCommandTab(type, Title);

						CommandTabBox cmdBox = cmdTab.AddCommandTabBox();

						int[] cmdIDs = new int[2];
						int[] TextType = new int[2];

						cmdIDs[0] = cmdGroup.get_CommandID(cmdIndex[0]);

						TextType[0] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

						cmdIDs[1] = cmdGroup.get_CommandID(cmdIndex[1]);

						TextType[1] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

						bResult = cmdBox.AddCommands(cmdIDs, TextType);

						cmdTab.AddSeparator(cmdBox, cmdIDs[0]);

					}

				}

				// Create a third-party icon in the context-sensitive menus of faces in parts
				// To see this menu, right click on any face in the part
				Frame swFrame;

				swFrame = iSwApp.Frame();

				string[] imageList = new string[4];
				imageList[0] = iconsFolderLoc + @"\icon16x16.bmp";
				imageList[1] = iconsFolderLoc + @"\icon20x20.bmp";
				imageList[2] = iconsFolderLoc + @"\icon32x32.bmp";
				imageList[3] = iconsFolderLoc + @"\icon40x40.bmp";
				bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocPART, (int)swSelectType_e.swSelFACES, "Find Component", addinID,
																										"PopupCallbackFunction", "PopupEnable", "", imageList);


				bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocASSEMBLY, (int)swSelectType_e.swSelFACES, "Find Component", addinID,
				 "PopupCallbackFunction", "PopupEnable", "", imageList);
				LogFileWriter.Write($"Leo AI Solidworks Addin Load End: ");
			}

			catch (Exception e)
			{
				//Log Error message
				LogFileWriter.Write($"an error: {e}");
			}
		}

		public void RemoveCommandMgr()
		{
			iBmp.Dispose();

			iCmdMgr.RemoveCommandGroup(mainCmdGroupID);
		}

		public bool CompareIDs(int[] storedIDs, int[] addinIDs)
		{
			List<int> storedList = new List<int>(storedIDs);
			List<int> addinList = new List<int>(addinIDs);

			addinList.Sort();
			storedList.Sort();

			if (addinList.Count != storedList.Count)
			{
				return false;
			}
			else
			{

				for (int i = 0; i < addinList.Count; i++)
				{
					if (addinList[i] != storedList[i])
					{
						return false;
					}
				}
			}
			return true;
		}

		#endregion

		#region UI Callbacks	
		public async void PopupCallbackFunction()
		{
			bool bRet;

			LogFileWriter.Write($"Leo AI -  Search Part Intiated from Pop-up menu: ");
			bRet = iSwApp.ShowThirdPartyPopupMenu(registerID, 500, 500);
			await SolidWorksHelper.OpenElectronApp("Leo is starting. Part retrieval coming online");
			////launch Electron app
			//await SolidWorksHelper.OpenElectronApp(loadingText);
			//Process the selected object 
			SolidWorksHelper.ProcessSelectedObject();

		}

		public int PopupEnable()
		{
			if (iSwApp.ActiveDoc == null)
				return 0;
			else
				return 1;
		}

		/// <summary>
		/// Enables the Search Command only if there is active document and Leo is running
		/// </summary>
		/// <returns></returns>
		public int EnableOrDisableSearchPart()
		{
			if (iSwApp.ActiveDoc != null && SolidWorksHelper.IsElectronAppRunning())
			{
				//Active document is present in solidworks
				//And Leo is running
				return 1;
			}
			else
			{
				return 0;
			}	
		}

		/// <summary>
		/// Launchs the Leo Application
		/// </summary>
		public async void LaunchLeoApp()
		{
			//Launch Electron app
			await SolidWorksHelper.OpenElectronApp("Leo is starting...");
		}

		/// <summary>
		/// Search selected part in the Leo AI App
		/// </summary>
		public async void SearchPart()
		{
			LogFileWriter.Write($"Leo AI -  Search Part Intiated from Menu/Ribbon: ");

			////launch Electron app
			await SolidWorksHelper.OpenElectronApp("Leo is starting. Part retrieval coming online");
			//If face already selected process the selection..
			if (SolidWorksHelper != null && SolidWorksHelper.IsFaceSelected())
			{
				SolidWorksHelper.ProcessSelectedObject();
			}
			else
			{
				PromptUserToSelectFace();
			}
		}

		public void PromptUserToSelectFace()
		{
			LogFileWriter.Write($"Leo AI -  Search Part PMP page start for user choice: ");
			ppage = new UserPMPage(this);
			ppage.Show();
		}
		#endregion

		#region Event Methods
		public bool AttachEventHandlers()
		{
			AttachSwEvents();
			//Listen for events on all currently open docs
			AttachEventsToAllDocuments();
			return true;
		}

		private bool AttachSwEvents()
		{
			try
			{
				SwEventPtr.ActiveDocChangeNotify += new DSldWorksEvents_ActiveDocChangeNotifyEventHandler(OnDocChange);
				SwEventPtr.DocumentLoadNotify2 += new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocLoad);
				SwEventPtr.FileNewNotify2 += new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNew);
				SwEventPtr.ActiveModelDocChangeNotify += new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnModelChange);
				SwEventPtr.FileOpenPostNotify += new DSldWorksEvents_FileOpenPostNotifyEventHandler(FileOpenPostNotify);
				return true;
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				return false;
			}
		}



		private bool DetachSwEvents()
		{
			try
			{
				SwEventPtr.ActiveDocChangeNotify -= new DSldWorksEvents_ActiveDocChangeNotifyEventHandler(OnDocChange);
				SwEventPtr.DocumentLoadNotify2 -= new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocLoad);
				SwEventPtr.FileNewNotify2 -= new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNew);
				SwEventPtr.ActiveModelDocChangeNotify -= new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnModelChange);
				SwEventPtr.FileOpenPostNotify -= new DSldWorksEvents_FileOpenPostNotifyEventHandler(FileOpenPostNotify);
				return true;
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				return false;
			}

		}

		public void AttachEventsToAllDocuments()
		{
			ModelDoc2 modDoc = (ModelDoc2)iSwApp.GetFirstDocument();
			while (modDoc != null)
			{
				if (!openDocs.Contains(modDoc))
				{
					AttachModelDocEventHandler(modDoc);
				}
				else if (openDocs.Contains(modDoc))
				{
					bool connected = false;
					DocumentEventHandler docHandler = (DocumentEventHandler)openDocs[modDoc];
					if (docHandler != null)
					{
						connected = docHandler.ConnectModelViews();
					}
				}

				modDoc = (ModelDoc2)modDoc.GetNext();
			}
		}

		public bool AttachModelDocEventHandler(ModelDoc2 modDoc)
		{
			if (modDoc == null)
				return false;

			DocumentEventHandler docHandler = null;

			if (!openDocs.Contains(modDoc))
			{
				switch (modDoc.GetType())
				{
					case (int)swDocumentTypes_e.swDocPART:
						{
							docHandler = new PartEventHandler(modDoc, this);
							break;
						}
					case (int)swDocumentTypes_e.swDocASSEMBLY:
						{
							docHandler = new AssemblyEventHandler(modDoc, this);
							break;
						}
					case (int)swDocumentTypes_e.swDocDRAWING:
						{
							docHandler = new DrawingEventHandler(modDoc, this);
							break;
						}
					default:
						{
							return false; //Unsupported document type
						}
				}
				docHandler.AttachEventHandlers();
				openDocs.Add(modDoc, docHandler);
			}
			return true;
		}

		public bool DetachModelEventHandler(ModelDoc2 modDoc)
		{
			DocumentEventHandler docHandler;
			docHandler = (DocumentEventHandler)openDocs[modDoc];
			openDocs.Remove(modDoc);
			modDoc = null;
			docHandler = null;
			return true;
		}

		public bool DetachEventHandlers()
		{
			DetachSwEvents();

			//Close events on all currently open docs
			DocumentEventHandler docHandler;
			int numKeys = openDocs.Count;
			object[] keys = new Object[numKeys];

			//Remove all document event handlers
			openDocs.Keys.CopyTo(keys, 0);
			foreach (ModelDoc2 key in keys)
			{
				docHandler = (DocumentEventHandler)openDocs[key];
				docHandler.DetachEventHandlers(); //This also removes the pair from the hash
				docHandler = null;
			}
			return true;
		}
		#endregion

		#region Event Handlers
		//Events
		public int OnDocChange()
		{
			return 0;
		}

		public int OnDocLoad(string docTitle, string docPath)
		{
			return 0;
		}

		int FileOpenPostNotify(string FileName)
		{
			AttachEventsToAllDocuments();
			return 0;
		}

		public int OnFileNew(object newDoc, int docType, string templateName)
		{
			AttachEventsToAllDocuments();
			return 0;
		}

		public int OnModelChange()
		{
			return 0;
		}
		
		#endregion

	}

}
