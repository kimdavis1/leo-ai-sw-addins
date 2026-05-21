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
using System.Threading.Tasks;
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

		// Separate command group IDs for each command
		public const int cmdGroupID_TurnLeo = 6;
		public const int cmdGroupID_FindComponent = 7;
		public const int cmdGroupID_PartReplacer = 8;
		public const int cmdGroupID_AssemblyInspector = 9;
		
		public const int mainItemID1 = 0; // Turn Leo on (in cmdGroupID_TurnLeo)
		public const int mainItemID2 = 0; // Find Component (in cmdGroupID_FindComponent)
		public const int mainItemID3 = 0; // Part Replacer (in cmdGroupID_PartReplacer)
		public const int mainItemID4 = 0; // Assembly Inspector (in cmdGroupID_AssemblyInspector)
		private int _cmdGroupID = 5;

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
			if (iBmp == null)
				iBmp = new BitmapHandler();
			//Initialize the helper class to support Solidworks  actions
			SolidWorksHelper = new SwHelper(iSwApp);
			string Title = "Leo AI";
			string iconsFolderLoc = AssemblyLocation + @"\Icons";

			//Addin exists in Part and assembly environment only..
			int[] docTypes = new int[] { (int)swDocumentTypes_e.swDocPART , (int)swDocumentTypes_e.swDocASSEMBLY};

			bool ignorePrevious = true; // Force ignore to clear any cached registry data

			// Create Command Group 1: Turn Leo On
			int errors = 0;
			ICommandGroup cmdGroupTurnLeo = iCmdMgr.CreateCommandGroup2(cmdGroupID_TurnLeo, "Turn Leo On", "Launch Leo AI application", "", -1, ignorePrevious, ref errors);
			
			string[] turnLeoIcons = new string[2];
			turnLeoIcons[0] = iconsFolderLoc + @"\icon16x16.bmp";
			turnLeoIcons[1] = iconsFolderLoc + @"\icon32x32.bmp";
			// Verify icon files exist
			if (File.Exists(turnLeoIcons[0]) && File.Exists(turnLeoIcons[1]))
			{
				cmdGroupTurnLeo.IconList = turnLeoIcons;
				LogFileWriter.Write($"Leo AI - Turn Leo On icons loaded: {turnLeoIcons[0]}, {turnLeoIcons[1]}");
			}
			else
			{
				LogFileWriter.Write($"Leo AI - Warning: Turn Leo On icon files not found!");
			}
			
			int menuToolbarOption = (int)(swCommandItemType_e.swToolbarItem | swCommandItemType_e.swMenuItem);
			int cmdIndexTurnLeo = cmdGroupTurnLeo.AddCommandItem2(
				"Turn Leo on",
				-1,
				"Turn Leo on",
				"Turn Leo On",
				0,  // Icon index: uses IconList[0] and IconList[1]
				"LaunchLeoApp",
				"",
				mainItemID1,
				menuToolbarOption
			);

			cmdGroupTurnLeo.HasToolbar = true;
			cmdGroupTurnLeo.HasMenu = true;
			cmdGroupTurnLeo.Activate();

			// Create Command Group 2: Find Component
			errors = 0;
			ICommandGroup cmdGroupFindComponent = iCmdMgr.CreateCommandGroup2(cmdGroupID_FindComponent, "Find Component", "Geometry based component search", "", -1, ignorePrevious, ref errors);
			
			string[] findComponentIcons = new string[2];
			findComponentIcons[0] = iconsFolderLoc + @"\findComponent16x16.png";
			findComponentIcons[1] = iconsFolderLoc + @"\findComponent32x32.png";
			// Verify icon files exist
			if (File.Exists(findComponentIcons[0]) && File.Exists(findComponentIcons[1]))
			{
				cmdGroupFindComponent.IconList = findComponentIcons;
				LogFileWriter.Write($"Leo AI - Find Component icons loaded: {findComponentIcons[0]}, {findComponentIcons[1]}");
			}
			else
			{
				LogFileWriter.Write($"Leo AI - Warning: Find Component icon files not found!");
			}
			
			int cmdIndexFindComponent = cmdGroupFindComponent.AddCommandItem2(
				"Find Component",
				-1,
				"Geometry based component search",
				"Find Component",
				0,  // Icon index: uses IconList[0] and IconList[1]
				"SearchPart",
				"",
				mainItemID2,
				menuToolbarOption
			);

			cmdGroupFindComponent.HasToolbar = true;
			cmdGroupFindComponent.HasMenu = true;
			cmdGroupFindComponent.Activate();

			// Create Command Group 3: Part Replacer
			errors = 0;
			ICommandGroup cmdGroupPartReplacer = iCmdMgr.CreateCommandGroup2(cmdGroupID_PartReplacer, "Part Replacer", "Replace selected part in assembly", "", -1, ignorePrevious, ref errors);
			
			string[] partReplacerIcons = new string[2];
			partReplacerIcons[0] = iconsFolderLoc + @"\partReplacer16x16.png";
			partReplacerIcons[1] = iconsFolderLoc + @"\partReplacer32x32.png";
			// Verify icon files exist
			if (File.Exists(partReplacerIcons[0]) && File.Exists(partReplacerIcons[1]))
			{
				cmdGroupPartReplacer.IconList = partReplacerIcons;
				LogFileWriter.Write($"Leo AI - Part Replacer icons loaded: {partReplacerIcons[0]}, {partReplacerIcons[1]}");
			}
			else
			{
				LogFileWriter.Write($"Leo AI - Warning: Part Replacer icon files not found!");
			}
			
			int cmdIndexPartReplacer = cmdGroupPartReplacer.AddCommandItem2(
				"Part Replacer",
				-1,
				"Replace selected part in assembly",
				"Part Replacer",
				0,  // Icon index: uses IconList[0] and IconList[1]
				"ReplacePart",
				"",
				mainItemID3,
				menuToolbarOption
			);

			cmdGroupPartReplacer.HasToolbar = true;
			cmdGroupPartReplacer.HasMenu = true;
			cmdGroupPartReplacer.Activate();

			// Create Command Group 4: Assembly Inspector
			errors = 0;
			ICommandGroup cmdGroupAssemblyInspector = iCmdMgr.CreateCommandGroup2(cmdGroupID_AssemblyInspector, "Assembly Inspector", "Inspect selected assembly", "", -1, ignorePrevious, ref errors);
			
			string[] assemblyInspectorIcons = new string[2];
			assemblyInspectorIcons[0] = iconsFolderLoc + @"\assemblyInspector16x16.png";
			assemblyInspectorIcons[1] = iconsFolderLoc + @"\assemblyInspector32x32.png";
			// Verify icon files exist
			if (File.Exists(assemblyInspectorIcons[0]) && File.Exists(assemblyInspectorIcons[1]))
			{
				cmdGroupAssemblyInspector.IconList = assemblyInspectorIcons;
				LogFileWriter.Write($"Leo AI - Assembly Inspector icons loaded: {assemblyInspectorIcons[0]}, {assemblyInspectorIcons[1]}");
			}
			else
			{
				LogFileWriter.Write($"Leo AI - Warning: Assembly Inspector icon files not found!");
			}
			
			int cmdIndexAssemblyInspector = cmdGroupAssemblyInspector.AddCommandItem2(
				"Assembly Inspector",
				-1,
				"Inspect selected assembly",
				"Assembly Inspector",
				0,  // Icon index: uses IconList[0] and IconList[1]
				"InspectAssembly",
				"EnableOrDisableInspectAssembly",
				mainItemID4,
				menuToolbarOption
			);

			cmdGroupAssemblyInspector.HasToolbar = true;
			cmdGroupAssemblyInspector.HasMenu = true;
			cmdGroupAssemblyInspector.Activate();

			// Add all commands to the ribbon tab
			bool bResult;

			foreach (int type in docTypes)
			{
				CommandTab cmdTab;
				cmdTab = iCmdMgr.GetCommandTab(type, Title);

				if (cmdTab != null) // If tab exists, remove it to recreate
				{
					bool res = iCmdMgr.RemoveCommandTab(cmdTab);
					cmdTab = null;
				}

				// Create the tab and add all commands from different groups
				cmdTab = iCmdMgr.AddCommandTab(type, Title);
				CommandTabBox cmdBox = cmdTab.AddCommandTabBox();

				int[] cmdIDs = new int[4];
				int[] TextType = new int[4];

				cmdIDs[0] = cmdGroupTurnLeo.get_CommandID(cmdIndexTurnLeo);
				TextType[0] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

				cmdIDs[1] = cmdGroupFindComponent.get_CommandID(cmdIndexFindComponent);
				TextType[1] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

				cmdIDs[2] = cmdGroupPartReplacer.get_CommandID(cmdIndexPartReplacer);
				TextType[2] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

				cmdIDs[3] = cmdGroupAssemblyInspector.get_CommandID(cmdIndexAssemblyInspector);
				TextType[3] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

				bResult = cmdBox.AddCommands(cmdIDs, TextType);
				cmdTab.AddSeparator(cmdBox, cmdIDs[0]);
			}

				// Create a third-party icon in the context-sensitive menus of faces in parts
				// To see this menu, right click on any face in the part
				Frame swFrame;

				swFrame = iSwApp.Frame();

			// Context menu icons for "Find Component" - uses .png format
			string[] imageListFindComponent = new string[4];
			imageListFindComponent[0] = iconsFolderLoc + @"\findComponent16x16.png";
			imageListFindComponent[1] = iconsFolderLoc + @"\findComponent20x20.png";
			imageListFindComponent[2] = iconsFolderLoc + @"\findComponent32x32.png";
			imageListFindComponent[3] = iconsFolderLoc + @"\findComponent40x40.png";
			// Verify icon files exist
			if (!File.Exists(imageListFindComponent[0]) || !File.Exists(imageListFindComponent[1]) || 
			    !File.Exists(imageListFindComponent[2]) || !File.Exists(imageListFindComponent[3]))
			{
				LogFileWriter.Write($"Leo AI - Warning: Find Component context menu icon files not found!");
			}
			else
			{
				LogFileWriter.Write($"Leo AI - Find Component context menu icons loaded successfully");
			}
			
			// Context menu icons for "Part Replacer" - uses .png format
			string[] imageListPartReplacer = new string[4];
			imageListPartReplacer[0] = iconsFolderLoc + @"\partReplacer16x16.png";
			imageListPartReplacer[1] = iconsFolderLoc + @"\partReplacer20x20.png";
			imageListPartReplacer[2] = iconsFolderLoc + @"\partReplacer32x32.png";
			imageListPartReplacer[3] = iconsFolderLoc + @"\partReplacer40x40.png";
			// Verify icon files exist
			if (!File.Exists(imageListPartReplacer[0]) || !File.Exists(imageListPartReplacer[1]) || 
			    !File.Exists(imageListPartReplacer[2]) || !File.Exists(imageListPartReplacer[3]))
			{
				LogFileWriter.Write($"Leo AI - Warning: Part Replacer context menu icon files not found!");
			}
			else
			{
				LogFileWriter.Write($"Leo AI - Part Replacer context menu icons loaded successfully");
			}
			
			// Context menu icons for "Assembly Inspector" - uses .png format
			string[] imageListAssemblyInspector = new string[4];
			imageListAssemblyInspector[0] = iconsFolderLoc + @"\assemblyInspector16x16.png";
			imageListAssemblyInspector[1] = iconsFolderLoc + @"\assemblyInspector20x20.png";
			imageListAssemblyInspector[2] = iconsFolderLoc + @"\assemblyInspector32x32.png";
			imageListAssemblyInspector[3] = iconsFolderLoc + @"\assemblyInspector40x40.png";
			// Verify icon files exist
			if (!File.Exists(imageListAssemblyInspector[0]) || !File.Exists(imageListAssemblyInspector[1]) || 
			    !File.Exists(imageListAssemblyInspector[2]) || !File.Exists(imageListAssemblyInspector[3]))
			{
				LogFileWriter.Write($"Leo AI - Warning: Assembly Inspector context menu icon files not found!");
			}
			else
			{
				LogFileWriter.Write($"Leo AI - Assembly Inspector context menu icons loaded successfully");
			}
			
			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocPART, (int)swSelectType_e.swSelFACES, "Find Component", addinID,
																									"PopupCallbackFunction", "PopupEnable", "", imageListFindComponent);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocASSEMBLY, (int)swSelectType_e.swSelFACES, "Find Component", addinID,
			 "PopupCallbackFunction", "PopupEnable", "", imageListFindComponent);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocPART, (int)swSelectType_e.swSelFACES, "Part Replacer", addinID,
																									"PartReplacerPopupCallback", "PartReplacerPopupEnable", "", imageListPartReplacer);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocASSEMBLY, (int)swSelectType_e.swSelFACES, "Part Replacer", addinID,
			 "PartReplacerPopupCallback", "PartReplacerPopupEnable", "", imageListPartReplacer);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocASSEMBLY, (int)swSelectType_e.swSelFACES, "Assembly Inspector", addinID,
			 "AssemblyInspectorPopupCallback", "AssemblyInspectorPopupEnable", "", imageListAssemblyInspector);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocASSEMBLY, (int)swSelectType_e.swSelCOMPONENTS, "Find Component", addinID,
			 "FindComponentTreeCallback", "FindComponentTreeEnable", "", imageListFindComponent);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocASSEMBLY, (int)swSelectType_e.swSelCOMPONENTS, "Part Replacer", addinID,
			 "PartReplacerTreeCallback", "PartReplacerTreeEnable", "", imageListPartReplacer);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocPART, (int)swSelectType_e.swSelCOMPONENTS, "Find Component", addinID,
			 "FindComponentTreeCallback", "FindComponentTreeEnable", "", imageListFindComponent);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocPART, (int)swSelectType_e.swSelCOMPONENTS, "Part Replacer", addinID,
			 "PartReplacerTreeCallback", "PartReplacerTreeEnable", "", imageListPartReplacer);

			bResult = swFrame.AddMenuPopupIcon3((int)swDocumentTypes_e.swDocASSEMBLY, (int)swSelectType_e.swSelCOMPONENTS, "Assembly Inspector", addinID,
			 "AssemblyInspectorTreeCallback", "AssemblyInspectorTreeEnable", "", imageListAssemblyInspector);
			}

			catch (Exception e)
			{
				//Log Error message
				LogFileWriter.Write($"an error: {e}");
			}
		}

		public void RemoveCommandMgr()
		{
			if (iBmp != null)
				iBmp.Dispose();

			// Remove all command groups
			iCmdMgr.RemoveCommandGroup(cmdGroupID_TurnLeo);
			iCmdMgr.RemoveCommandGroup(cmdGroupID_FindComponent);
			iCmdMgr.RemoveCommandGroup(cmdGroupID_PartReplacer);
			iCmdMgr.RemoveCommandGroup(cmdGroupID_AssemblyInspector);
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
		
		ModelDoc2 swModel = (ModelDoc2)iSwApp.ActiveDoc;
		if (swModel != null)
		{
			swModel.ClearSelection2(true);
			LogFileWriter.Write($"Leo AI - Cleared selection before showing PMP");
		}
		
		ppage = new UserPMPage(this, PMPMode.FindComponent);
		ppage.Show();
	}

	/// <summary>
	/// Shows Property Manager Page for user to select a part/component for replacement
	/// </summary>
	public void PromptUserToSelectPart()
	{
		LogFileWriter.Write($"Leo AI - Part Replacer PMP page start for user choice: ");
		
		ModelDoc2 swModel = (ModelDoc2)iSwApp.ActiveDoc;
		if (swModel != null)
		{
			swModel.ClearSelection2(true);
			LogFileWriter.Write($"Leo AI - Cleared selection before showing PMP for Part Replacer");
		}
		
		ppage = new UserPMPage(this, PMPMode.PartReplacer);
		ppage.Show();
	}

	/// <summary>
	/// Part Replacer popup callback - called from face context menu
	/// </summary>
	public void PartReplacerPopupCallback()
	{
		LogFileWriter.Write($"Leo AI - Part Replacer initiated from face pop-up menu: ");
		ReplacePartCommand(); // Fire and forget - method is async void
	}

	/// <summary>
	/// Enable/disable Part Replacer in face popup menu
	/// </summary>
	/// <returns></returns>
	public int PartReplacerPopupEnable()
	{
		if (iSwApp.ActiveDoc == null)
			return 0;
		else
			return 1;
	}

	/// <summary>
	/// Assembly Inspector popup callback - called from face context menu
	/// </summary>
	public void AssemblyInspectorPopupCallback()
	{
		LogFileWriter.Write($"Leo AI - Assembly Inspector initiated from face pop-up menu: ");
		InspectAssemblyCommand();
	}

	/// <summary>
	/// Enable/disable Assembly Inspector in face popup menu
	/// </summary>
	/// <returns></returns>
	public int AssemblyInspectorPopupEnable()
	{
		if (iSwApp.ActiveDoc == null)
			return 0;
		
		ModelDoc2 activeDoc = (ModelDoc2)iSwApp.ActiveDoc;
		
		if (activeDoc is AssemblyDoc)
		{
			return 1;
		}
		
		return 0;
	}

	/// <summary>
	/// Find Component callback from feature tree context menu
	/// </summary>
	public void FindComponentTreeCallback()
	{
		LogFileWriter.Write($"Leo AI - Find Component initiated from feature tree context menu");
		try
		{
			SearchPart(); // Fire and forget - method is async void
		}
		catch (Exception ex)
		{
			LogFileWriter.Write($"Leo AI - Error in FindComponentTreeCallback: {ex.Message}");
		}
	}

	/// <summary>
	/// Enable/disable Find Component in feature tree context menu
	/// </summary>
	/// <returns></returns>
	public int FindComponentTreeEnable()
	{
		if (iSwApp.ActiveDoc != null)
		{
			return 1;
		}
		else
		{
			return 0;
		}
	}

	/// <summary>
	/// Part/Assembly Replacer callback from feature tree context menu
	/// </summary>
	public void PartReplacerTreeCallback()
	{
		LogFileWriter.Write($"Leo AI - Part/Assembly Replacer initiated from feature tree context menu");
		
		if (SolidWorksHelper == null)
		{
			LogFileWriter.Write($"Leo AI - Part Replacer: SolidWorksHelper is null");
			return;
		}

		ModelDoc2 activeDoc = (ModelDoc2)iSwApp.ActiveDoc;
		if (activeDoc == null)
		{
			LogFileWriter.Write($"Leo AI - Part Replacer: No active document");
			return;
		}

		// ReplacePartCommand now handles both parts and assemblies
		ReplacePartCommand(); // Fire and forget - method is async void
	}

	/// <summary>
	/// Enable/disable Part Replacer in feature tree context menu
	/// </summary>
	/// <returns></returns>
	public int PartReplacerTreeEnable()
	{
		if (iSwApp.ActiveDoc == null || SolidWorksHelper == null)
		{
			return 0;
		}

		if (SolidWorksHelper.CanGetSelectedPartOrAssemblyPath())
		{
			return 1;
		}

		ModelDoc2 activeDoc = (ModelDoc2)iSwApp.ActiveDoc;
		if (activeDoc is AssemblyDoc)
		{
			SelectionMgr selMgr = activeDoc.SelectionManager as SelectionMgr;
			if (selMgr != null && selMgr.GetSelectedObjectCount2(-1) > 0)
			{
				object selectedObject = selMgr.GetSelectedObject6(1, -1);
				if (selectedObject is Component2)
				{
					return 1;
				}
			}
		}

		return 0;
	}

	/// <summary>
	/// Assembly Inspector callback from feature tree context menu
	/// </summary>
	public void AssemblyInspectorTreeCallback()
	{
		LogFileWriter.Write($"Leo AI - Assembly Inspector initiated from feature tree context menu");
		InspectAssemblyCommand();
	}

	/// <summary>
	/// Enable/disable Assembly Inspector in feature tree context menu
	/// </summary>
	/// <returns></returns>
	public int AssemblyInspectorTreeEnable()
	{
		if (iSwApp.ActiveDoc == null || SolidWorksHelper == null)
		{
			return 0;
		}

		ModelDoc2 activeDoc = (ModelDoc2)iSwApp.ActiveDoc;
		
		if (activeDoc is AssemblyDoc)
		{
			SelectionMgr selMgr = activeDoc.SelectionManager as SelectionMgr;
			if (selMgr != null && selMgr.GetSelectedObjectCount2(-1) > 0)
			{
				object selectedObject = selMgr.GetSelectedObject6(1, -1);
				if (selectedObject is Component2 selectedComponent)
				{
					ModelDoc2 componentDoc = selectedComponent.GetModelDoc2() as ModelDoc2;
					if (componentDoc != null && componentDoc is AssemblyDoc)
					{
						return 1;
					}
					else if (componentDoc != null && componentDoc is PartDoc)
					{
						return 0;
					}
				}
			}
			
			return 0;
		}
		
		return 0;
	}

	/// <summary>
	/// Enables the Part/Assembly Replacer Command only if there is active document and a part or assembly is selected
	/// </summary>
	/// <returns></returns>
	public int EnableOrDisableReplacePart()
	{
		if (iSwApp.ActiveDoc != null && SolidWorksHelper != null && SolidWorksHelper.CanGetSelectedPartOrAssemblyPath())
		{
			return 1;
		}
		else
		{
			return 0;
		}
	}

	/// <summary>
	/// Enables the Assembly Inspector Command only if there is active document and an assembly is selected
	/// </summary>
	/// <returns></returns>
	public int EnableOrDisableInspectAssembly()
	{
		if (iSwApp.ActiveDoc == null || SolidWorksHelper == null)
		{
			return 0;
		}

		return SolidWorksHelper.GetAssemblyDocumentForInspection() != null ? 1 : 0;
	}

	/// <summary>
	/// Part/Assembly Replacer callback from ribbon button - checks if part or assembly is selected and sends to Leo app
	/// </summary>
	public void ReplacePart()
	{
		LogFileWriter.Write($"Leo AI - Part/Assembly Replacer button pressed on ribbon bar");
		
		if (SolidWorksHelper == null)
		{
			LogFileWriter.Write($"Leo AI - Part Replacer: SolidWorksHelper is null");
			iSwApp.SendMsgToUser2("Error: SolidWorks helper is not initialized.", (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
			return;
		}

		if (!SolidWorksHelper.CanGetSelectedPartOrAssemblyPath())
		{
			LogFileWriter.Write($"Leo AI - Part Replacer: No part or assembly selected, showing PMP for selection");
			PromptUserToSelectPart();
			return;
		}

		ReplacePartCommand();
	}

	public void InspectAssembly()
	{
		LogFileWriter.Write($"Leo AI - Assembly Inspector button pressed on ribbon bar");
		
		if (SolidWorksHelper == null)
		{
			LogFileWriter.Write($"Leo AI - Assembly Inspector: SolidWorksHelper is null");
			iSwApp.SendMsgToUser2("Error: SolidWorks helper is not initialized.", (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
			return;
		}

		InspectAssemblyCommand();
	}

	/// <summary>
	/// Part/Assembly replacement workflow - initiates part or assembly replacement in Leo app
	/// </summary>
	public async void ReplacePartCommand()
	{
		LogFileWriter.Write($"Leo AI - Part/Assembly Replacer initiated from Menu/Ribbon: ");

		try
		{
			// Launch Leo desktop app
			await SolidWorksHelper.OpenElectronApp("Leo is starting...");

			ModelDoc2 activeDoc = (ModelDoc2)iSwApp.ActiveDoc;
			bool isAssembly = false;
			string filePath = null;

			if (activeDoc != null)
			{
				SelectionMgr selMgr = activeDoc.SelectionManager as SelectionMgr;
				if (selMgr != null && selMgr.GetSelectedObjectCount2(-1) > 0)
				{
					object selectedObject = selMgr.GetSelectedObject6(1, -1);
					if (selectedObject is Component2 selectedComponent)
					{
						ModelDoc2 componentDoc = selectedComponent.GetModelDoc2() as ModelDoc2;
						if (componentDoc != null)
						{
							if (componentDoc is AssemblyDoc)
							{
								isAssembly = true;
								filePath = SolidWorksHelper.GetSelectedAssemblyFilePath();
								LogFileWriter.Write($"Leo AI - Part Replacer: Selected component is an assembly");
							}
							else if (componentDoc is PartDoc)
							{
								isAssembly = false;
								filePath = SolidWorksHelper.GetSelectedPartFilePath();
								LogFileWriter.Write($"Leo AI - Part Replacer: Selected component is a part");
							}
						}
					}
				}
			}

			if (string.IsNullOrEmpty(filePath))
			{
				filePath = SolidWorksHelper.GetSelectedPartFilePath();
				if (string.IsNullOrEmpty(filePath))
				{
					// Try assembly path as fallback
					filePath = SolidWorksHelper.GetSelectedAssemblyFilePath();
					if (!string.IsNullOrEmpty(filePath))
					{
						isAssembly = true;
					}
				}
			}

			if (!string.IsNullOrEmpty(filePath))
			{
				// Check if the file exists on disk
				if (!File.Exists(filePath))
				{
					string docType = isAssembly ? "assembly" : "part";
					LogFileWriter.Write($"Leo AI - {docType} file not saved to disk, prompting user to save: {filePath}");
					
					if (activeDoc != null)
					{
						ModelDoc2 docToSave = null;
						
						SelectionMgr selMgr = activeDoc.SelectionManager as SelectionMgr;
						if (selMgr != null && selMgr.GetSelectedObjectCount2(-1) > 0)
						{
							object selectedObj = selMgr.GetSelectedObject6(1, -1);
							if (selectedObj is Component2 comp)
							{
								docToSave = comp.GetModelDoc2() as ModelDoc2;
							}
							else if (selectedObj is Face2 face)
							{
								Component2 compFromFace = selMgr.GetSelectedObjectsComponent4(1, -1) as Component2;
								if (compFromFace != null)
								{
									docToSave = compFromFace.GetModelDoc2() as ModelDoc2;
								}
							}
						}
						
						if (docToSave == null)
						{
							docToSave = activeDoc;
						}
						
						if (docToSave != null)
						{
							string docPath = docToSave.GetPathName();
							if (string.IsNullOrEmpty(docPath) || !File.Exists(docPath))
							{
								// Document is not saved, prompt user
								string message = isAssembly 
									? "The assembly document needs to be saved before replacing. Do you want to save it now?"
									: "The part document needs to be saved before replacing. Do you want to save it now?";
								int result = iSwApp.SendMsgToUser2(message, (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbYesNo);
								if (result == (int)swMessageBoxResult_e.swMbHitYes)
								{
									if (docToSave != activeDoc)
									{
										string docTitle = docToSave.GetTitle();
										iSwApp.ActivateDoc3(docTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
									}
									
									docToSave.Save();
									filePath = docToSave.GetPathName();
									
									if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
									{
										LogFileWriter.Write($"Leo AI - Document saved successfully: {filePath}");
										
										if (docToSave != activeDoc)
										{
											iSwApp.ActivateDoc3(activeDoc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
										}
									}
									else
									{
										string errorMsg = isAssembly 
											? "Failed to save the assembly document. Assembly replacement cancelled."
											: "Failed to save the document. Part replacement cancelled.";
										iSwApp.SendMsgToUser2(errorMsg, (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
										LogFileWriter.Write($"Leo AI - Failed to save document for replacement");
										return;
									}
								}
								else
								{
									LogFileWriter.Write($"Leo AI - User cancelled saving document for replacement");
									return;
								}
							}
							else
							{
								filePath = docPath;
							}
						}
					}
				}

				if (File.Exists(filePath))
				{
					await LeoWebClientHelper.SendPartReplacementRequest(filePath);
					LogFileWriter.Write($"Leo AI - {(isAssembly ? "Assembly" : "Part")} replacement request sent: {filePath}");
				}
				else
				{
					string docType = isAssembly ? "Assembly" : "Part";
					iSwApp.SendMsgToUser2($"{docType} file not found: {filePath}. Please save the document first.", (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
					LogFileWriter.Write($"Leo AI - {docType} file does not exist: {filePath}");
				}
			}
			else
			{
				iSwApp.SendMsgToUser2("No part or assembly selected. Please select a part or assembly in the feature tree to replace.", (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
				LogFileWriter.Write($"Leo AI - No part or assembly selected for replacement");
			}
		}
		catch (Exception ex)
		{
			LogFileWriter.Write($"Leo AI - Error in ReplacePartCommand: {ex.Message}");
			iSwApp.SendMsgToUser2($"Error initiating part replacement: {ex.Message}", (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
		}
	}

	/// <summary>
	/// Sends part replacement request to Leo app asynchronously (used by PMPHandler)
	/// </summary>
	/// <param name="partFilePath">Full path to the part file</param>
	public async Task SendPartReplacementRequestAsync(string partFilePath)
	{
		try
		{
			LogFileWriter.Write($"Leo AI - Part Replacer: Sending part replacement request for: {partFilePath}");

			await SolidWorksHelper.OpenElectronApp("Leo is starting...");

			if (!File.Exists(partFilePath))
			{
				LogFileWriter.Write($"Leo AI - Part file not saved to disk: {partFilePath}");
				iSwApp.SendMsgToUser2($"Part file not found: {partFilePath}. Please save the document first.", (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
				return;
			}

			await LeoWebClientHelper.SendPartReplacementRequest(partFilePath);
			LogFileWriter.Write($"Leo AI - Part Replacer: Part replacement request sent successfully");
		}
		catch (Exception ex)
		{
			LogFileWriter.Write($"Leo AI - Error in SendPartReplacementRequestAsync: {ex.Message}");
			iSwApp.SendMsgToUser2($"Error sending part replacement request: {ex.Message}", (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
		}
	}

	/// <summary>
	/// Assembly inspection workflow - initiates assembly inspection in Leo app
	/// </summary>
	public async void InspectAssemblyCommand()
	{
		LogFileWriter.Write($"Leo AI - Assembly Inspector initiated from Menu/Ribbon");

		try
		{
			await SolidWorksHelper.OpenElectronApp("Leo is starting...");

			ModelDoc2 activeDoc = (ModelDoc2)iSwApp.ActiveDoc;
			AssemblyDoc assemblyDoc = SolidWorksHelper.GetAssemblyDocumentForInspection();
			if (assemblyDoc == null)
			{
				iSwApp.SendMsgToUser2("No assembly to inspect. Open an assembly document, or select a subassembly in the feature tree.", (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
				LogFileWriter.Write($"Leo AI - No assembly document context for inspection");
				return;
			}

			ModelDoc2 assemblyModelDoc = (ModelDoc2)assemblyDoc;
			string assemblyFilePath = null;
			if (!TryEnsureAssemblySavedForInspection(activeDoc, assemblyModelDoc, ref assemblyFilePath))
			{
				return;
			}

			if (File.Exists(assemblyFilePath))
			{
				await LeoWebClientHelper.SendAssemblyInspectionRequest(assemblyFilePath);
			}
			else
			{
				iSwApp.SendMsgToUser2($"Assembly file not found: {assemblyFilePath}. Please save the document first.", (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
				LogFileWriter.Write($"Leo AI - Assembly file does not exist: {assemblyFilePath}");
			}
		}
		catch (Exception ex)
		{
			LogFileWriter.Write($"Leo AI - Error in InspectAssemblyCommand: {ex.Message}");
			iSwApp.SendMsgToUser2($"Error initiating assembly inspection: {ex.Message}", (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
		}
	}

	/// <summary>
	/// Ensures the assembly has a path on disk (prompts to save when new, unsaved, or file missing).
	/// </summary>
	private bool TryEnsureAssemblySavedForInspection(ModelDoc2 activeDoc, ModelDoc2 assemblyModelDoc, ref string assemblyFilePath)
	{
		if (assemblyModelDoc == null || !(assemblyModelDoc is AssemblyDoc))
		{
			return false;
		}

		assemblyFilePath = assemblyModelDoc.GetPathName();
		if (!string.IsNullOrEmpty(assemblyFilePath) && File.Exists(assemblyFilePath))
		{
			return true;
		}

		LogFileWriter.Write($"Leo AI - Assembly file not on disk (path empty or missing), prompting user to save: '{assemblyFilePath ?? ""}'");

		int result = iSwApp.SendMsgToUser2("The assembly document needs to be saved before inspection. Do you want to save it now?", (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbYesNo);
		if (result != (int)swMessageBoxResult_e.swMbHitYes)
		{
			LogFileWriter.Write($"Leo AI - User cancelled saving assembly document for inspection");
			return false;
		}

		if (activeDoc != null && assemblyModelDoc != activeDoc)
		{
			string docTitle = assemblyModelDoc.GetTitle();
			iSwApp.ActivateDoc3(docTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
		}

		assemblyModelDoc.Save();
		assemblyFilePath = assemblyModelDoc.GetPathName();

		if (!string.IsNullOrEmpty(assemblyFilePath) && File.Exists(assemblyFilePath))
		{
			LogFileWriter.Write($"Leo AI - Assembly document saved successfully: {assemblyFilePath}");
			if (activeDoc != null && assemblyModelDoc != activeDoc)
			{
				iSwApp.ActivateDoc3(activeDoc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
			}
			return true;
		}

		iSwApp.SendMsgToUser2("Failed to save the assembly document. Assembly inspection cancelled.", (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
		LogFileWriter.Write($"Leo AI - Failed to save assembly document for inspection");
		return false;
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
