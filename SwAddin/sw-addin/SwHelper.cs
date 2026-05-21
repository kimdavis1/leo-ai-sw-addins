using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using sw_addin.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using SolidWorks.Interop.swcommands;
using sw_addin.Logs;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Net.NetworkInformation;

namespace sw_addin
{
	public enum DocType
	{
		Part,
		Assembly,
		Drawing,
		Empty
	}
	public class SwHelper
	{
		SldWorks solidWorksApplication { get; set; }
		ModelDoc2 solidworksDocument { get; set; }

		Measure solidworksMeasure { get; set; }

		#region Native Window References

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		static extern bool IsIconic(IntPtr hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool IsWindowVisible(IntPtr hWnd);
		#endregion

		private const int SW_RESTORE = 9;

		private const string loadingText = "Leo is starting...";

		private const string ElectronAppPath = @"C:\Program Files\Leo\Leo.exe";// @"Leo.exe";
		public SwHelper(ISldWorks iSwApp)
		{
			solidWorksApplication = (SldWorks)iSwApp;
		}

		public SwHelper()
		{
		}


		/// <summary>
		/// Does Necessary actions on the selected Object(Component/face) of Active Document
		/// </summary>
		public async void ProcessSelectedObject()
		{
			ModelDoc2 swModel = solidWorksApplication.ActiveDoc;
			if (swModel == null)
			{
				LogFileWriter.Write($"Leo AI -  Active dcoument not exists in Solidworks: ");
				//No active document opened in SolidWorks
				return;
			}

			solidworksDocument = (ModelDoc2)swModel;	
			LogFileWriter.Write($"Leo AI - Active mode in Solidworks : {swModel.GetPathName()}: ");
			solidworksMeasure = swModel.Extension.CreateMeasure();

			//get selection manager 
			SelectionMgr selMgr = swModel.SelectionManager as SelectionMgr;
			if (selMgr == null)
			{
				//Failed to get SelectionManager";
				return;
			}
			//get the selected objects count
			int selCount = selMgr.GetSelectedObjectCount2(-1);

			if (selCount == 0)
			{
				LogFileWriter.Write($"Leo AI - Selection is Empty in  {swModel.GetPathName()}: ");
				//No object selected in active document
				return;
			}
			//get the selected object
			object selectedObject = selMgr.GetSelectedObject6(1, -1);

			if (selectedObject is Component2)
			{
				LogFileWriter.Write($"Leo AI - Selection is a Component. ");
				//TODO:: Do necessary actions on selected component
			}
			else if (selectedObject is Face2)
			{
				LogFileWriter.Write($"Leo AI - Selection is a Face: ");
				ModelDoc2 componentDoc = null;

				// If we're in an assembly, get the component
				Component2 component = selMgr.GetSelectedObjectsComponent4(1, -1) as Component2;
				componentDoc = component?.GetModelDoc2() ?? swModel;

				await MeasureSelectedFaceAndSendData(selectedObject as Face2, componentDoc);
			}
		}

		/// <summary>
		/// Gets Respective data from the face and sends it
		/// </summary>
		/// <param name="selectedFace"></param>
		/// <param name="componentDoc"></param>
		/// <returns></returns>
		public async Task MeasureSelectedFaceAndSendData(Face2 selectedFace, ModelDoc2 componentDoc)
		{
			if (selectedFace == null)
			{
				Debug.Print("No face selected. Please select a face and try again.");
				LogFileWriter.Write($"Leo AI - Selection is not a Face. ");
				return;
			}
			if (componentDoc == null)
			{
				Debug.Print("Unable to get the model document for the component.");
				LogFileWriter.Write($"Leo AI - Unable to get component from selected face. ");
				return;
			}

			//get units
			string unitSystem = GetDocUnitSystem(componentDoc);

			LogFileWriter.Write($"Leo AI - Unit System  :{unitSystem}. ");

			solidworksMeasure.ArcOption = 0; // 0 = center to center

			bool status = solidworksMeasure.Calculate(null);
			if (status)
			{
				var measurementData = CollectMeasurementData(selectedFace, unitSystem);

				LogFileWriter.LogMeasureProperties(measurementData);
				await LeoWebClientHelper.SendMeasurementData(measurementData);
			}
			else
			{
				Debug.Print("Failed to calculate measurements for the selected face.");
			}
		}


		/// <summary>
		/// Gets Required Data from the selected face
		/// </summary>
		/// <param name="swMeasure"></param>
		/// <param name="selectedFace"></param>
		/// <param name="originalUnits"></param>
		/// <returns></returns>
		private SwMeasurementData CollectMeasurementData(Face2 selectedFace, string originalUnits)
		{
			SwUnitsConverter.CurrentDocUnits = originalUnits;
			bool isHole = IsFaceIsAHole(selectedFace);
			HoleInfo selectedHoleInfo = null;
			if (isHole)
			{
				LogFileWriter.Write($"Leo AI - Selected a Face contians Hole Feature. ");
				//selected object is an hole
				//get the hole related info
				selectedHoleInfo = ExtractHoleFeatureData(selectedFace);
			}
			return new SwMeasurementData
			{
				Area = SwUnitsConverter.ConvertAndFormat(solidworksMeasure.Area),
				Perimeter = SwUnitsConverter.ConvertAndFormat(solidworksMeasure.Perimeter),
				Radius = SwUnitsConverter.ConvertAndFormat(solidworksMeasure.Radius),
				Diameter = SwUnitsConverter.ConvertAndFormat(solidworksMeasure.Diameter),
				CenterPoint = new Point3D
				{
					X = SwUnitsConverter.ConvertAndFormat(solidworksMeasure.X),
					Y = SwUnitsConverter.ConvertAndFormat(solidworksMeasure.Y),
					Z = SwUnitsConverter.ConvertAndFormat(solidworksMeasure.Z)
				},
				Normal = SwUnitsConverter.ConvertAndFormat(solidworksMeasure.Normal),
				IsHole = isHole,
				surfaceType = GetSurfaceTypeName(selectedFace.GetSurface() as Surface),
				SelectedHoleInfo = selectedHoleInfo
			};
		}

		/// <summary>
		/// Get Document unit system
		/// </summary>
		/// <param name="modelDoc"></param>
		/// <returns></returns>
		private string GetDocUnitSystem(ModelDoc2 swModelDoc)
		{
			int unitType = (int)swUserPreferenceIntegerValue_e.swUnitSystem;
			int unitSystem = swModelDoc.Extension.GetUserPreferenceInteger(unitType, (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);

			switch (unitSystem)
			{
				case 1: // CGS
					return "cm";
				case 2: // MKS
					return "m";
				case 3: // IPS
					return "in";
				case 4: // custom todo - the default is mm for now, we need to pull the units when the case is custom
					return "mm";
				case 5: //MMGS
					return "mm";
				default:
					Debug.Print($"Unit type is unknown");
					return "";
			}
		}


		/// <summary>
		/// Get Surface Type based on input surface
		/// </summary>
		/// <param name="surface"></param>
		/// <returns></returns>
		private string GetSurfaceTypeName(Surface surface)
		{
			if (surface.IsPlane())
				return "Plane";
			else if (surface.IsCylinder())
				return "Cylinder";
			else if (surface.IsCone())
				return "Cone";
			else if (surface.IsSphere())
				return "Sphere";
			else if (surface.IsTorus())
				return "Torus";
			else
				return "Unknown";
		}

		/// <summary>
		/// Loads a non-native file (for example, *.igs, *.dxf, *.prt, *.sat, *.ipt, *.step) into a new model document in SOLIDWORKS.
		/// </summary>
		/// <param name="importFilePath"></param>
		/// <returns></returns>
		public bool LoadFile(string importFilePath)
		{

			if (solidWorksApplication == null)
			{
				return false;
			}
			//Get import information
			ImportStepData swImportStepData = (ImportStepData)solidWorksApplication.GetImportFileData(importFilePath);

			//If ImportStepData::MapConfigurationData is not set, then default to
			//the environment setting swImportStepConfigData; otherwise, override
			//swImportStepConfigData with ImportStepData::MapConfigurationData
			swImportStepData.MapConfigurationData = true;
		
			int errors = 0;		
			//Import the STEP file
			PartDoc swPart = (PartDoc)solidWorksApplication.LoadFile4(importFilePath, "r", swImportStepData, ref errors);
			ModelDoc2 swModel = (ModelDoc2)swPart;
			ModelDocExtension swModelDocExt = (ModelDocExtension)swModel.Extension;

			//Run diagnostics on the STEP file and repair any bad faces
			errors = swPart.ImportDiagnosis(true, false, true, 0);

			return swPart != null;
		}



		/// <summary>
		/// Opens the provided file in SolidWorks
		/// </summary>
		/// <param name="swFilePath"></param>
		/// <returns></returns>
		public bool OpenDocument(string swFilePath)
		{
			if (solidWorksApplication == null)
			{
				return false;
			}
			try
			{
				LogFileWriter.Write($"Leo AI -  Open - {swFilePath}  - Start: ");
				solidWorksApplication.FrameState = (int)swWindowState_e.swWindowMaximized;
				//get document specification
				DocumentSpecification swDocSpecification = solidWorksApplication.GetOpenDocSpec(swFilePath);
				//open the require ddocument in solidworks
				ModelDoc2 SWModel = solidWorksApplication.OpenDoc7(swDocSpecification);
				if (SWModel == null)
				{
					//failed to open the document
					return false;
				}
				if (SWModel is AssemblyDoc)
				{
					ModelView myModelView = null;
					myModelView = SWModel.ActiveView;
					//open the software window maximize 
					myModelView.FrameState = (int)swWindowState_e.swWindowMaximized;

					if (SWModel.Extension.ViewDisplayRealView)
						SWModel.Extension.ViewDisplayRealView = false;
				}
				//Succesfully opened the document in solidworks

				LogFileWriter.Write($"Leo AI -  Open - {swFilePath}  - End: ");
				return true;
			}
			catch (Exception ex)
			{
				////exception occurred 
				///Failed to open the document
				LogFileWriter.Write($"Leo AI -  Open Failed- {swFilePath}  - {ex.Message}. ");
			}
			return false;
		}

		public bool OpenInsertComponentDialog(string swFilePath)
		{
			bool isDialogOpened = false;
			ModelDoc2 activeModelDoc = (ModelDoc2)solidWorksApplication.ActiveDoc;
			LogFileWriter.Write($"Leo AI - Insert - Opening Insert Component dialog. File path provided: {swFilePath ?? "null"}");
			
			if (activeModelDoc is AssemblyDoc)
			{
				AssemblyDoc swAsmDoc = (AssemblyDoc)activeModelDoc;
				string title = activeModelDoc.GetTitle();
				
				LogFileWriter.Write($"Leo AI - Insert - Active document is an assembly: {title}");
				
				solidWorksApplication.ActivateDoc3(title, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
				
				LogFileWriter.Write($"Leo AI - Insert - Triggering Insert Component dialog...");
				solidWorksApplication.RunCommand((int)swCommands_e.swCommands_InsertComponents, "");
				
				LogFileWriter.Write($"Leo AI - Insert - Insert Component dialog opened. User can now browse and select file manually.");
				isDialogOpened = true;
			}
			else
			{
				LogFileWriter.Write($"Leo AI - Insert - ERROR: Active document is not an assembly. Type: ");
			}
			
			return isDialogOpened;
		}
		/// <summary>
		/// Insert Non-native file into active assmembly document
		/// </summary>
		/// <param name="stepFilePath"></param>
		/// <returns></returns>
		public bool InsertNonNativeFile(string stepFilePath)
		{
			bool isSuccess = false;

			if (solidWorksApplication == null)
			{
				return isSuccess;
			}

			LogFileWriter.Write($"Leo AI -  Insert Non-native Files - {stepFilePath}  - Start: ");
			ModelDoc2 activeModelDoc = solidWorksApplication.ActiveDoc;
			if (activeModelDoc is AssemblyDoc assemblyDoc)
			{
				object CompObj;
				int error = assemblyDoc.InsertImportedComponent(stepFilePath, 0, 0, 0, out CompObj);
				if (CompObj != null)
				{
					isSuccess = true;
					Component2 insertedComp = (Component2)CompObj;
					//bool isFixed = comp.IsFixed();
					//bool isSelected = comp.Select2(false, 0);
					//if (isSelected && isFixed) //Delete ground relation
					//{
					//	assemblyDoc.UnfixComponent();
					//}
					if (insertedComp != null)
					{
						insertedComp.Select2(false, 0);
						//activeModelDoc.ViewZoomToSelection();
						bool isInserted = solidWorksApplication.RunCommand((int)swCommands_e.swCommands_Mate, "");

						LogFileWriter.Write($"Leo AI -  Insert Non-native Files - {stepFilePath}  - End: ");
					}
				}
			}

			return isSuccess;
		}

		/// <summary>
		/// Inserts the provided file in the Active document in SolidWorks
		/// </summary>
		/// <param name="swFilePath"></param>
		public bool Insert(string swFilePath)
		{
			bool isDocInserted = false;
			//get the active document
			ModelDoc2 activeModelDoc = (ModelDoc2)solidWorksApplication.ActiveDoc;
			LogFileWriter.Write($"Leo AI -  Insert native File - {swFilePath}  - Start: ");
			if (activeModelDoc is AssemblyDoc)
			{
				AssemblyDoc swAsmDoc = (AssemblyDoc)activeModelDoc;
				string assemblyDocName = Path.GetFileNameWithoutExtension(activeModelDoc.GetPathName());
				string title = activeModelDoc.GetTitle();
				//Open the document that needs to be inserted

				ModelDoc2 insertingDoc = solidWorksApplication.GetOpenDocumentByName(swFilePath);
				if (insertingDoc == null)
				{
					DocumentSpecification swDocSpecification = solidWorksApplication.GetOpenDocSpec(swFilePath);
					insertingDoc = solidWorksApplication.OpenDoc7(swDocSpecification);
					if (insertingDoc == null)
					{
						//No doc present to insert
						return isDocInserted;
					}
				}
				// activate the current assembly
				solidWorksApplication.ActivateDoc3(title, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
				//insert the opened document into the active assembly

				solidWorksApplication.RunCommand((int)swCommands_e.swCommands_InsertComponents, "");
				//Component2 insertedComp = swAsmDoc.AddComponent4(swFilePath, "", 0, 0, 0);
				////Note: By default inserted component relation is grounded
				////Delete the grounded relation of the inserted Document
				////bool isFixed = cmp.IsFixed();
				////bool isSelected = cmp.Select2(false, 0);
				////if (isSelected && isFixed)
				////{//Delete ground relation
				////	swAsmDoc.UnfixComponent();
				////}
				///
				//if (insertedComp != null)
				//{
				//	insertedComp.Select2(false, 0);
				//	//activeModelDoc.ViewZoomToSelection();
				//	bool isInserted = solidWorksApplication.RunCommand((int)swCommands_e.swCommands_Mate, "");
				//	LogFileWriter.Write($"Leo AI -  Insert native File - {swFilePath}  - End: ");
				//}
				solidWorksApplication.CloseDoc(insertingDoc.GetTitle());
				isDocInserted = true;

			}
			return isDocInserted;
		}

		/// <summary>
		/// Check given input face is a Hole face or not
		/// </summary>
		/// <param name="face"></param>
		/// <returns></returns>
		private bool IsFaceIsAHole(Face2 face)
		{
			Surface surf = face.GetSurface() as Surface;
			if (surf.IsCylinder())
			{
				LogFileWriter.Write($"Leo AI -  Selected Face is a Cylindircal. ");
				double[] uvBounds = face.GetUVBounds() as double[];
				double[] evalData = surf.Evaluate((uvBounds[1] - uvBounds[0]) / 2, (uvBounds[3] - uvBounds[2]) / 2, 1, 1) as double[];
				double[] pt = new double[] { evalData[0], evalData[1], evalData[2] };
				int sense = face.FaceInSurfaceSense() ? 1 : -1;
				double[] norm = new double[] { evalData[evalData.Length - 3] * sense, evalData[evalData.Length - 2] * sense, evalData[evalData.Length - 1] * sense };
				double[] cylParams = surf.CylinderParams as double[];
				double[] orig = new double[] { cylParams[0], cylParams[1], cylParams[2] };
				double[] dir = new double[] { pt[0] - orig[0], pt[1] - orig[1], pt[2] - orig[2] };

				IMathUtility mathUtils = solidWorksApplication.GetMathUtility();
				IMathVector dirVec = mathUtils.CreateVector(dir) as IMathVector;
				IMathVector normVec = mathUtils.CreateVector(norm) as IMathVector;
				Debug.Print("Selected face is a hole");
				return GetAngle(dirVec, normVec) < Math.PI / 2;
			}
			else
			{//Face contains ant hole feature..
				IFeature feature = face.GetFeature();
				if (feature == null)
				{
					return false;
				}
				string featureType = feature.GetTypeName2();

				switch (featureType)
				{
					case "HoleWzd":
					case "SimpleHole":
					case "AdvHoleWzd":
						{
							LogFileWriter.Write($"Leo AI -  Selected Face contains hole feature. ");
							return true;
						}
				}


				Debug.Print("Selected face is not cylindrical.");
				return false;
			}
		}


		/// <summary>
		/// Gets all the available info for the Hole
		/// </summary>
		/// <param name="holeType"></param>
		/// <param name="holeInfo"></param>
		/// <param name="holeWizardData"></param>
		public void GetHoleInfoProperties(swWzdHoleTypes_e holeType, HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			IHoleType hole = HoleTypeFactory.CreateHoleType(holeType);
			hole.GetHoleProperties(holeInfo, holeWizardData);

			holeInfo.HoleType = holeType.ToString();

			//Depth
			holeInfo.Depth = SwUnitsConverter.ConvertAndFormat(holeWizardData.Depth);

			//These properties are not support of swTapered and swTaperedDrilled holes.
			if (holeType != swWzdHoleTypes_e.swTapered || holeType != swWzdHoleTypes_e.swTaperedDrilled)
			{
				holeInfo.Diameter = SwUnitsConverter.ConvertAndFormat(holeWizardData.HoleDiameter);
				holeInfo.ThruHoleDiameter = SwUnitsConverter.ConvertAndFormat(holeWizardData.ThreadDiameter);
			}

			//End condition
			swEndConditions_e swEndConditions_E = (swEndConditions_e)holeWizardData.EndCondition;
			holeInfo.EndCondition = swEndConditions_E.ToString();

			//Fastener Size			
			holeInfo.FastenerSize = holeWizardData.FastenerSize;

			//Fastener Type
			swWzdHoleStandardFastenerTypes_e swWzdHoleStandardFastenerTypes_E = (swWzdHoleStandardFastenerTypes_e)holeWizardData.FastenerType2;
			holeInfo.FastenerType = swWzdHoleStandardFastenerTypes_E.ToString();


			//Head Clearnce
			holeInfo.HeadClearance = SwUnitsConverter.ConvertAndFormat(holeWizardData.HeadClearance);

			swWzdHoleCounterSinkHeadClearanceTypes_e swWzdHoleCounterSinkHeadClearanceTypes_E = (swWzdHoleCounterSinkHeadClearanceTypes_e)holeWizardData.HeadClearanceType;
			//Head Clearnce Type
			holeInfo.HeadClearanceType = swWzdHoleCounterSinkHeadClearanceTypes_E.ToString();


			//Length of Slot
			holeInfo.SlotLength = SwUnitsConverter.ConvertAndFormat(holeWizardData.Length);

			//OffSet Distance
			holeInfo.OffSetDistance = SwUnitsConverter.ConvertAndFormat(holeWizardData.OffsetDistance);

			//Reverse Direction
			holeInfo.ReverseDirection = holeWizardData.ReverseDirection;

			//Hole Deisgn standard for this Hole Wizard feature
			swWzdHoleStandards_e swWzdHoleStandards_E = (swWzdHoleStandards_e)holeWizardData.Standard2;
			//If the Wizard Hole is using a copied/custom standard, then this property returns -1. In that case, use IWizardHoleFeatureData2::Standard to get the copied/custom standard.
			holeInfo.Standard = swWzdHoleStandards_E.ToString();
			if ((int)swWzdHoleStandards_E == -1)
			{
				holeInfo.Standard = holeWizardData.Standard;
			}


			//Tap Type
			swWzdHoleHcoilTapTypes_e swWzdHoleHcoilTapTypes_E = (swWzdHoleHcoilTapTypes_e)holeWizardData.TapType;
			holeInfo.TapType = swWzdHoleHcoilTapTypes_E.ToString();


			//Thread Angle
			//This property is relevant only for threaded holes.		
			switch (holeType)
			{
				case swWzdHoleTypes_e.swTapBlind:
				case swWzdHoleTypes_e.swTapThru:
				case swWzdHoleTypes_e.swTapBlindCosmeticThread:
				case swWzdHoleTypes_e.swTapThruCosmeticThread:
				case swWzdHoleTypes_e.swTapThruThreadThru:
				case swWzdHoleTypes_e.swTapThruThreadThruCounterSinkTop:
				case swWzdHoleTypes_e.swTapThruThreadThruCounterSinkBottom:
				case swWzdHoleTypes_e.swTapThruThreadThruCountersinkTopBottom:
				case swWzdHoleTypes_e.swTapBlindRemoveThread:
				case swWzdHoleTypes_e.swPipeTapBlind:
				case swWzdHoleTypes_e.swPipeTapBlindCounterSinkTop:
				case swWzdHoleTypes_e.swPipeTapThru:
				case swWzdHoleTypes_e.swPipeTapThruCounterSinkBottom:
				case swWzdHoleTypes_e.swPipeTapThruCounterSinkTop:
				case swWzdHoleTypes_e.swPipeTapThruCounterSinkTopBottom:
					{
						holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThreadAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.ThreadAngle)));
						break;
					}
				default:
					break;
			}

			//Thread Class
			//This property is relevant only for the ANSI inch standard.
			if (swWzdHoleStandards_E == swWzdHoleStandards_e.swStandardAnsiInch)
			{
				holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThreadClass ", holeWizardData.ThreadClass));
			}

			//Thread Depth
			holeInfo.ThreadDepth = SwUnitsConverter.ConvertAndFormat(holeWizardData.ThreadDepth);

			//Thread Diameter
			holeInfo.ThredDiameter = SwUnitsConverter.ConvertAndFormat(holeWizardData.ThreadDiameter);

			//Thread End Condition
			swWzdHoleThreadEndCondition_e swWzdHoleThreadEndCondition_E = (swWzdHoleThreadEndCondition_e)holeWizardData.ThreadEndCondition;
			holeInfo.ThreadEndCondition = swWzdHoleThreadEndCondition_E.ToString();

			//ThruHoleDepth
			holeInfo.ThruHoleDepth = SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruHoleDepth);


		}

		/// <summary>
		/// Gets the respective data of the selected hole on the face
		/// </summary>
		/// <param name="selectedFace"></param>
		private HoleInfo ExtractHoleFeatureData(Face2 selectedFace)
		{
			if (selectedFace == null)
			{
				return null;
			}

			// Get the feature associated with the face
			IFeature feature = selectedFace.GetFeature();

			if (feature == null)
			{
				return null;
			}
			string featureType = feature.GetTypeName2();
			HoleInfo holeInfo = new HoleInfo();
			switch (featureType)
			{
				case "HoleWzd":
					{ // Extract Hole Wizard data based on hole type..
						IWizardHoleFeatureData2 holeWizardData = feature.GetDefinition() as IWizardHoleFeatureData2;
						holeWizardData.AccessSelections(solidworksDocument, null);
						swWzdHoleTypes_e swWzdHoleTypes_E = (swWzdHoleTypes_e)holeWizardData.Type;
						GetHoleInfoProperties(swWzdHoleTypes_E, holeInfo, (WizardHoleFeatureData2)holeWizardData);
						holeWizardData.ReleaseSelectionAccess();
						break;
					}
				case "SimpleHole":
					{
						// Extract Simple Hole data
						ISimpleHoleFeatureData2 simpleHoleData = feature.GetDefinition() as ISimpleHoleFeatureData2;
						simpleHoleData.AccessSelections(solidworksDocument, null);
						holeInfo.Diameter = SwUnitsConverter.ConvertAndFormat(simpleHoleData.Diameter);
						holeInfo.Depth = SwUnitsConverter.ConvertAndFormat(simpleHoleData.Depth);
						holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("DraftAngle", SwUnitsConverter.RadiansToDegree(simpleHoleData.DraftAngle)));
						holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("SurfaceOffset", SwUnitsConverter.ConvertAndFormat(simpleHoleData.SurfaceOffset)));
						swEndConditions_e swEndConditions_E = (swEndConditions_e)simpleHoleData.Type;
						holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("Type", swEndConditions_E.ToString()));
						simpleHoleData.ReleaseSelectionAccess();
						holeInfo.HoleType = "Simple Hole";
						break;
					}
				case "AdvHoleWzd":
					{
						holeInfo.HoleType = "Advanced Hole";
						IAdvancedHoleFeatureData advancedHoleData = feature.GetDefinition() as IAdvancedHoleFeatureData;
						advancedHoleData.AccessSelections(solidworksDocument, null);
						int farElementCount = 1;
						int nearElementCount = 1;
						object[] farSideElements = (object[])advancedHoleData.GetFarSideElements();
						object[] nearSideElements = (object[])advancedHoleData.GetNearSideElements();
						string nearElementPropPrefix = "Near_";
						string farElementPropPrefix = "Far_";
						//Near side elements  in case far side
						foreach (object obj in nearSideElements)
						{
							string propPrefix = nearElementPropPrefix + nearElementCount.ToString();
							IAdvancedHoleElementData advData = (IAdvancedHoleElementData)obj;
							// get the required information from the Hole Element
							swAdvWzdGeneralHoleTypes_e elementType = (swAdvWzdGeneralHoleTypes_e)advData.ElementType;

							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_ElementType", elementType.ToString()));
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_Diameter", SwUnitsConverter.ConvertAndFormat(advData.Diameter)));

							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_Size", advData.Size));
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_BlindDepth", SwUnitsConverter.ConvertAndFormat(advData.BlindDepth)));

							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_OffsetDistance", SwUnitsConverter.ConvertAndFormat(advData.OffsetDistance)));

							swEndConditions_e swEndConditions_E = (swEndConditions_e)advData.EndCondition;
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_EndCondition", swEndConditions_E.ToString()));

							swHoleElementOrientation_e swHoleElementOrientation_E = (swHoleElementOrientation_e)advData.Orientation;
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_Orientation", swHoleElementOrientation_E.ToString()));

							swWzdHoleStandards_e holeStandard = (swWzdHoleStandards_e)advData.Standard;
							//get the  hole standard used for this hole element

							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_HoleStandard", holeStandard.ToString()));
							swWzdHoleStandardFastenerTypes_e fastenerType = (swWzdHoleStandardFastenerTypes_e)advData.FastenerType;

							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_FastenerType", fastenerType.ToString()));
							nearElementCount++;
						}

						//Far side elements  in case far side
						foreach (object obj in farSideElements)
						{
							string propPrefix = farElementPropPrefix + farElementCount.ToString();
							IAdvancedHoleElementData advData = (IAdvancedHoleElementData)obj;

							// get the required information from the Hole Element
							swAdvWzdGeneralHoleTypes_e elementType = (swAdvWzdGeneralHoleTypes_e)advData.ElementType;
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_ElementType", elementType.ToString()));
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_Diameter", SwUnitsConverter.ConvertAndFormat(advData.Diameter)));

							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_Size", advData.Size));
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_BlindDepth", SwUnitsConverter.ConvertAndFormat(advData.BlindDepth)));

							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_OffsetDistance", SwUnitsConverter.ConvertAndFormat(advData.OffsetDistance)));

							swEndConditions_e swEndConditions_E = (swEndConditions_e)advData.EndCondition;
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_EndCondition", swEndConditions_E.ToString()));

							swHoleElementOrientation_e swHoleElementOrientation_E = (swHoleElementOrientation_e)advData.Orientation;
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_Orientation", swHoleElementOrientation_E.ToString()));

							swWzdHoleStandards_e holeStandard = (swWzdHoleStandards_e)advData.Standard;
							//get the  hole standard used for this hole element
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_HoleStandard", holeStandard.ToString()));
							swWzdHoleStandardFastenerTypes_e fastenerType = (swWzdHoleStandardFastenerTypes_e)advData.FastenerType;
							holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>($"{propPrefix}_FastenerType", fastenerType.ToString()));
							farElementCount++;
						}
						advancedHoleData.ReleaseSelectionAccess();
						break;
					}
					//case "CosmeticThread":
					//	{
					//		CosmeticThreadFeatureData swCosThread = (CosmeticThreadFeatureData)feature.GetDefinition();
					//		holeInfo.HoleDepth = swCosThread.BlindDepth.ToString();
					//		holeInfo.HoleDiameter = swCosThread.Diameter.ToString();
					//		//swCosThread.DiameterType);
					//		//swCosThread.ThreadCallout);
					//		// swCosThread.ConfigurationOption);
					//		//swCosThread.EndCondition);
					//		holeInfo.ThreadSize = swCosThread.Size;
					//		// swCosThread.Standard;
					//		// swCosThread.StandardType;
					//		break;
					//	}

			}
			return holeInfo;
		}

		/// <summary>
		/// Get Angle between two vectors or faces
		/// </summary>
		/// <param name="vec1"></param>
		/// <param name="vec2"></param>
		/// <returns></returns>
		private double GetAngle(IMathVector vec1, IMathVector vec2)
		{
			return Math.Acos(vec1.Dot(vec2) / (vec1.GetLength() * vec2.GetLength()));
		}


		/// <summary>
		/// Check if Electron Application is Already Running
		/// </summary>
		/// <returns></returns>
		public bool IsElectronAppRunning()
		{
			Process[] processes = Process.GetProcessesByName("Leo");
			//Process mainProcess = processes.FirstOrDefault(p => IsWindowVisible(p.MainWindowHandle) && !string.IsNullOrEmpty(p.MainWindowTitle));
			//To avoid the issue with window hide mode
			if (processes.Length > 0 )
			{
				return true;
			}
			else
			{//Dev environment check
			  processes = Process.GetProcessesByName("electron");
			  //mainProcess = processes.FirstOrDefault(p => IsWindowVisible(p.MainWindowHandle) && !string.IsNullOrEmpty(p.MainWindowTitle));
			}
			return processes.Length > 0;
		}


		/// <summary>
		/// Launches the Electron Application
		/// </summary>
		/// <param name="launchText"></param>
		/// <returns></returns>
		public async Task OpenElectronApp(string launchText)
		{
			LoadingForm loadingForm = null;
			try
			{
				LogFileWriter.Write($"Leo AI -  Leo App run check Start: ");
				if (!IsElectronAppRunning())
				{
					LogFileWriter.Write($"Leo AI -  Leo App not running in the machine. ");
					//application is not running
					string AssemblyLocation = Path.GetDirectoryName(Assembly.GetAssembly(GetType()).Location);
					loadingForm = new LoadingForm(launchText, AssemblyLocation + @"\Icons" + @"\Logo_animation_Dark.gif");
					loadingForm.Show();
					if (launchText != loadingText)
					{
						await Task.Delay(3000);
					}

					LogFileWriter.Write($"Leo AI -  Leo App runing from {ElectronAppPath}: ");
					Process.Start(ElectronAppPath);
				}
				else
				{
					LogFileWriter.Write($"Leo AI -  Leo App already runing the machine:");
					//Application is already running
					//Active the running instance of the Electron App
					Process[] processes = Process.GetProcessesByName("Leo");

					if (processes.Length == 0)
					{
						//Dev build support...
						processes = Process.GetProcessesByName("electron");
					}
					foreach (Process process in processes)
					{
						IntPtr hWnd = process.MainWindowHandle;

						LogFileWriter.Write($"Leo AI -  Leo App Check if the window is minimized: ");
						// Check if the window is minimized
						if (IsIconic(hWnd))
						{
							LogFileWriter.Write($"Leo AI -   Restore the window if it's minimized: ");
							// Restore the window if it's minimized
							ShowWindow(hWnd, SW_RESTORE);
						}


						LogFileWriter.Write($"Leo AI -   Bring the window to the foreground: ");
						// Bring the window to the foreground
						SetForegroundWindow(hWnd);
					}

					SwMeasurementData swMeasurementData = new SwMeasurementData();
					await LeoWebClientHelper.SendMeasurementData(swMeasurementData, true);
				}

				LogFileWriter.Write($"Leo AI -  Leo App run check Start: ");
			}
			catch (Exception ex)
			{
				solidWorksApplication.SendMsgToUser($"Error: {ex.Message}");
				LogFileWriter.Write($"Leo AI -  {ex.Message} ");
			}
			finally
			{
				loadingForm?.Invoke(new Action(() => loadingForm.Close()));
			}
		}

		/// <summary>
		/// Check if input file is a supported file type based on file extension
		/// </summary>
		/// <param name="filePath"></param>
		/// <returns></returns>
		public bool IsProcessableFile(string filePath)
		{
			string extension = Path.GetExtension(filePath).ToUpperInvariant();
			
			// SOLIDWORKS files (excluding drawings)
			if (extension == ".SLDPRT" || extension == ".SLDASM")
				return true;
				
			// Generic CAD formats
			if (extension == ".STEP" || extension == ".STP")
				return true;
				
			// Parasolid files - temporarily disabled until API MIME type is confirmed
			// if (extension == ".X_T" || extension == ".XT")
			//     return true;
				
			// Document formats
			if (extension == ".TXT" || extension == ".PDF")
				return true;
				
			// Microsoft Word formats
			if (extension == ".DOC" || extension == ".DOCX")
				return true;
				
			return false;
		}

		/// <summary>
		/// Check input file is a Solidworks file or not based on file extension (legacy method)
		/// </summary>
		/// <param name="filePath"></param>
		/// <returns></returns>
		public bool IsSolidWorksFile(string filePath)
		{
			string extension = Path.GetExtension(filePath).ToUpperInvariant();
			return extension == ".SLDPRT" || extension == ".SLDDRW" || extension == ".SLDASM";
		}


		/// <summary>
		/// Get active document type (part/assembly/drawing)
		/// </summary>
		/// <returns></returns>
		internal DocType ActiveDocType()
		{
			DocType activeDocType = DocType.Empty;
			if (solidworksDocument == null)
			{ 
				solidworksDocument  = solidWorksApplication.ActiveDoc; 
			}

			int iDocType = solidworksDocument.GetType();

			swDocumentTypes_e docType = (swDocumentTypes_e)iDocType;

			switch (docType)
			{
				case swDocumentTypes_e.swDocNONE:
					break;
				case swDocumentTypes_e.swDocPART:
				case swDocumentTypes_e.swDocIMPORTED_PART:
					{
						activeDocType = DocType.Part;
						break;
					}
				case swDocumentTypes_e.swDocASSEMBLY:
				case swDocumentTypes_e.swDocIMPORTED_ASSEMBLY:
					{
						activeDocType = DocType.Assembly;
						break; 
					}
				case swDocumentTypes_e.swDocDRAWING:
					{
						activeDocType = DocType.Drawing;
						break;
					}
				case swDocumentTypes_e.swDocSDM:
					break;
				case swDocumentTypes_e.swDocLAYOUT:
					break;
			}

			return activeDocType;
		}


		/// <summary>
		/// Check Selected Entity is Face or not
		/// </summary>
		/// <returns></returns>
		internal bool IsFaceSelected()
		{
			ModelDoc2 swModel = solidWorksApplication.ActiveDoc;
			if (swModel == null)
			{
				//No active document opened in SolidWorks
				return false;
			}

			solidworksDocument = (ModelDoc2)swModel;
		

			//get selection manager 
			SelectionMgr selMgr = swModel.SelectionManager as SelectionMgr;
			if (selMgr == null)
			{
				//Failed to get SelectionManager";
				return false;
			}
			//get the selected objects count
			int selCount = selMgr.GetSelectedObjectCount2(-1);

			if (selCount == 0)
			{
				//No object selected in active document
				return false;
			}
			//get the selected object
			object selectedObject = selMgr.GetSelectedObject6(1, -1);

			if (selectedObject is Face2)
			{
				LogFileWriter.Write($"Leo AI -  Selected Entity is a Face. ");
				return true;
			}
			else if (selectedObject is Entity swEnt)
			{
				int tye = swEnt.GetType();
				swSelectType_e swSelectType_E = (swSelectType_e)tye;
				LogFileWriter.Write($"Leo AI -  Selected Entity is a {swSelectType_E.ToString()}. ");
			}

			return false;
		}

		/// <summary>
		/// Gets the file path of the selected part (from face selection or active part document)
		/// </summary>
		/// <returns>File path of the selected part, or null if no part can be determined</returns>
		public string GetSelectedPartFilePath()
		{
			ModelDoc2 swModel = solidWorksApplication.ActiveDoc;
			if (swModel == null)
			{
				LogFileWriter.Write($"Leo AI - Active document not exists in Solidworks");
				return null;
			}

			solidworksDocument = (ModelDoc2)swModel;

			SelectionMgr selMgr = swModel.SelectionManager as SelectionMgr;
			if (selMgr == null)
			{
				LogFileWriter.Write($"Leo AI - Failed to get SelectionManager");
				return null;
			}

			int selCount = selMgr.GetSelectedObjectCount2(-1);

			if (selCount > 0)
			{
				object selectedObject = selMgr.GetSelectedObject6(1, -1);

				if (selectedObject is Face2 selectedFace)
				{
					LogFileWriter.Write($"Leo AI - Selected entity is a Face for part replacement");
					Component2 component = selMgr.GetSelectedObjectsComponent4(1, -1) as Component2;
					if (component != null)
					{
						ModelDoc2 componentDoc = component.GetModelDoc2() as ModelDoc2;
						if (componentDoc != null)
						{
							string partPath = componentDoc.GetPathName();
							LogFileWriter.Write($"Leo AI - Part path from selected face component: {partPath}");
							return partPath;
						}
					}
					// If no component (in part document), use active document
					string docPath = swModel.GetPathName();
					LogFileWriter.Write($"Leo AI - Part path from active document: {docPath}");
					return docPath;
				}
				else if (selectedObject is Component2 selectedComponent)
				{
					LogFileWriter.Write($"Leo AI - Selected entity is a Component for part replacement");
					ModelDoc2 componentDoc = selectedComponent.GetModelDoc2() as ModelDoc2;
					if (componentDoc != null)
					{
						if (componentDoc is AssemblyDoc)
						{
							LogFileWriter.Write($"Leo AI - Selected component is an assembly, not a part. Cannot use Part Replacer.");
							return null;
						}
						else if (componentDoc is PartDoc)
						{
							string partPath = componentDoc.GetPathName();
							LogFileWriter.Write($"Leo AI - Part path from selected component: {partPath}");
							return partPath;
						}
					}
				}
			}

			// If no selection, check if active document is a part
			if (swModel is PartDoc)
			{
				string partPath = swModel.GetPathName();
				if (!string.IsNullOrEmpty(partPath))
				{
					LogFileWriter.Write($"Leo AI - Part path from active part document: {partPath}");
					return partPath;
				}
			}

			LogFileWriter.Write($"Leo AI - Could not determine part file path for replacement");
			return null;
		}

		/// <summary>
		/// Gets the file path of the selected assembly or the active assembly document
		/// </summary>
		/// <returns>Assembly file path, or null if not found</returns>
		public string GetSelectedAssemblyFilePath()
		{
			ModelDoc2 swModel = solidWorksApplication.ActiveDoc;
			if (swModel == null)
			{
				LogFileWriter.Write($"Leo AI - Active document does not exist in SolidWorks");
				return null;
			}

			solidworksDocument = (ModelDoc2)swModel;

			SelectionMgr selMgr = swModel.SelectionManager as SelectionMgr;
			if (selMgr == null)
			{
				LogFileWriter.Write($"Leo AI - Failed to get SelectionManager");
				return null;
			}

			int selCount = selMgr.GetSelectedObjectCount2(-1);

			if (selCount > 0)
			{
				object selectedObject = selMgr.GetSelectedObject6(1, -1);

				if (selectedObject is Component2 selectedComponent)
				{
					LogFileWriter.Write($"Leo AI - Selected entity is a Component for assembly inspection");
					ModelDoc2 componentDoc = selectedComponent.GetModelDoc2() as ModelDoc2;
					if (componentDoc != null)
					{
						if (componentDoc is AssemblyDoc)
						{
							string assemblyPath = componentDoc.GetPathName();
							LogFileWriter.Write($"Leo AI - Assembly path from selected component: {assemblyPath}");
							return assemblyPath;
						}
						else
						{
							LogFileWriter.Write($"Leo AI - Selected component is not an assembly, it's a part");
							return null;
						}
					}
				}
			}

			if (swModel is AssemblyDoc)
			{
				string assemblyPath = swModel.GetPathName();
				if (!string.IsNullOrEmpty(assemblyPath))
				{
					LogFileWriter.Write($"Leo AI - Assembly path from active assembly document: {assemblyPath}");
					return assemblyPath;
				}
			}

			LogFileWriter.Write($"Leo AI - Could not determine assembly file path for inspection");
			return null;
		}

		/// <summary>
		/// Checks if we can get a selected part path (for enabling/disabling Part Replacer button)
		/// </summary>
		/// <returns>True if a part path can be determined, false otherwise</returns>
		public bool CanGetSelectedPartPath()
		{
			string partPath = GetSelectedPartFilePath();
			return !string.IsNullOrEmpty(partPath);
		}

		/// <summary>
		/// Checks if we can get a selected part or assembly path (for enabling/disabling Part Replacer button)
		/// </summary>
		/// <returns>True if a part or assembly path can be determined, false otherwise</returns>
		public bool CanGetSelectedPartOrAssemblyPath()
		{
			// Check for part path first
			string partPath = GetSelectedPartFilePath();
			if (!string.IsNullOrEmpty(partPath))
			{
				return true;
			}
			
			// Check for assembly path
			string assemblyPath = GetSelectedAssemblyFilePath();
			return !string.IsNullOrEmpty(assemblyPath);
		}

		/// <summary>
		/// Replaces assemblies in assembly: finds assembly components by name or file path, removes them, and inserts new assembly
		/// </summary>
		/// <param name="placeLikeComponentTitle">Assembly component name to search for (first priority)</param>
		/// <param name="placeLikeFilePath">Assembly component file path to search for (fallback if name not found)</param>
		/// <param name="newAssemblyFilePath">Path to the new assembly file to insert</param>
		/// <param name="replaceExisting">If true, removes the original assembly after replacement</param>
		/// <returns>True if replacement was successful, false otherwise</returns>
		public bool ReplaceAssemblyInAssembly(string placeLikeComponentTitle, string placeLikeFilePath, string newAssemblyFilePath, bool replaceExisting = false)
		{
			try
			{
				LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - START");
				LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Parameters:");
				LogFileWriter.Write($"Leo AI :   - Title: {placeLikeComponentTitle ?? "null"}");
				LogFileWriter.Write($"Leo AI :   - FilePath: {placeLikeFilePath ?? "null"}");
				LogFileWriter.Write($"Leo AI :   - NewAssembly: {newAssemblyFilePath ?? "null"}");
				LogFileWriter.Write($"Leo AI :   - ReplaceExisting: {replaceExisting}");
				LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");

				if (solidWorksApplication == null)
				{
					LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - ERROR: SolidWorks application is null.");
					return false;
				}

				ModelDoc2 activeModelDoc = solidWorksApplication.ActiveDoc;
				if (activeModelDoc == null)
				{
					LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - ERROR: No active document.");
					return false;
				}

				AssemblyDoc assemblyDoc = activeModelDoc as AssemblyDoc;
				if (assemblyDoc == null)
				{
					LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - ERROR: Active document is not an assembly. Type: {activeModelDoc.GetType()}");
					return false;
				}

				List<Component2> allFoundComponents = FindComponentsToReplace(assemblyDoc, placeLikeComponentTitle, placeLikeFilePath);

				if (allFoundComponents == null)
				{
					LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - ERROR: FindComponentsToReplace returned null");
					return false;
				}

				List<Component2> assemblyComponentsToReplace = new List<Component2>();
				foreach (Component2 comp in allFoundComponents)
				{
					try
					{
						ModelDoc2 compDoc = comp.GetModelDoc2() as ModelDoc2;
						if (compDoc != null && compDoc is AssemblyDoc)
						{
							assemblyComponentsToReplace.Add(comp);
							LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Found assembly component: {comp.Name2}");
						}
					}
					catch (Exception compEx)
					{
						LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Error checking component type: {compEx.Message}");
					}
				}

				if (assemblyComponentsToReplace.Count == 0)
				{
					LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - WARNING: No assembly components found to replace.");
					return false;
				}

				List<Component2> uniqueComponents = new List<Component2>();
				HashSet<IntPtr> seenPointers = new HashSet<IntPtr>();
				foreach (Component2 comp in assemblyComponentsToReplace)
				{
					try
					{
						IntPtr compPtr = Marshal.GetIUnknownForObject(comp);
						if (!seenPointers.Contains(compPtr))
						{
							seenPointers.Add(compPtr);
							uniqueComponents.Add(comp);
							string compName = comp?.Name2 ?? "null";
							LogFileWriter.Write($"Leo AI :   - Unique assembly component: {compName}");
						}
						else
						{
							LogFileWriter.Write($"Leo AI :   - Skipping duplicate assembly component");
							Marshal.Release(compPtr);
						}
					}
					catch (Exception dupEx)
					{
						LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Error checking duplicate: {dupEx.Message}");
					}
				}
				assemblyComponentsToReplace = uniqueComponents;

				List<ComponentPlacementInfo> placementInfos = new List<ComponentPlacementInfo>();
				for (int i = 0; i < assemblyComponentsToReplace.Count; i++)
				{
					try
					{
						LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Getting placement info for assembly component {i + 1}...");
						ComponentPlacementInfo info = GetComponentPlacementInfo(assemblyComponentsToReplace[i]);
						placementInfos.Add(info);
					}
					catch (Exception infoEx)
					{
						LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - ERROR getting placement info for assembly component {i + 1}: {infoEx.Message}");
					}
				}
				LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Stored {placementInfos.Count} placement info(s)");

				List<Component2> insertedComponents = new List<Component2>();
				for (int i = 0; i < placementInfos.Count; i++)
				{
					try
					{
						LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Inserting assembly component {i + 1} of {placementInfos.Count}...");
						Component2 insertedComp = InsertComponentAtLocation(assemblyDoc, newAssemblyFilePath, placementInfos[i]);
						if (insertedComp != null)
						{
							insertedComponents.Add(insertedComp);
						}
					}
					catch (Exception insertEx)
					{
						LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - ERROR inserting assembly component {i + 1}: {insertEx.Message}");
					}
				}
				LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Assembly component insertion phase completed. Successfully inserted: {insertedComponents.Count} of {placementInfos.Count}");

				if (replaceExisting)
				{
					LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - ReplaceExisting is true, removing original assembly components...");
					for (int i = 0; i < assemblyComponentsToReplace.Count; i++)
					{
						try
						{
							LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - Removing assembly component {i + 1} of {assemblyComponentsToReplace.Count}...");
							RemoveComponent(assemblyDoc, assemblyComponentsToReplace[i]);
						}
						catch (Exception removeEx)
						{
							LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - ERROR removing assembly component {i + 1}: {removeEx.Message}");
						}
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : ReplaceAssemblyInAssembly - FATAL ERROR: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Replaces parts in assembly: finds components by name or file path, removes them, and inserts new part
		/// </summary>
		/// <param name="placeLikeComponentTitle">Component name to search for (first priority)</param>
		/// <param name="placeLikeFilePath">Component file path to search for (fallback if name not found)</param>
		/// <param name="newPartFilePath">Path to the new part file to insert</param>
		/// <returns>True if replacement was successful, false otherwise</returns>
		public bool ReplacePartInAssembly(string placeLikeComponentTitle, string placeLikeFilePath, string newPartFilePath, bool replaceExisting = false)
		{
			try
			{
				LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - START");
				LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Parameters:");
				LogFileWriter.Write($"Leo AI :   - Title: {placeLikeComponentTitle ?? "null"}");
				LogFileWriter.Write($"Leo AI :   - FilePath: {placeLikeFilePath ?? "null"}");
				LogFileWriter.Write($"Leo AI :   - NewPart: {newPartFilePath ?? "null"}");
				LogFileWriter.Write($"Leo AI :   - ReplaceExisting: {replaceExisting}");
				LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");

				if (solidWorksApplication == null)
				{
					return false;
				}

				ModelDoc2 activeModelDoc = solidWorksApplication.ActiveDoc;
				if (activeModelDoc == null)
				{
					return false;
				}

				AssemblyDoc assemblyDoc = activeModelDoc as AssemblyDoc;
				if (assemblyDoc == null)
				{
					return false;
				}

				// Find components to replace
				List<Component2> componentsToReplace = FindComponentsToReplace(assemblyDoc, placeLikeComponentTitle, placeLikeFilePath);

				if (componentsToReplace == null)
				{
					return false;
				}

				if (componentsToReplace.Count == 0)
				{
					return false;
				}

				// Remove duplicates from the list (same component reference)
				List<Component2> uniqueComponents = new List<Component2>();
				HashSet<IntPtr> seenPointers = new HashSet<IntPtr>();
				foreach (Component2 comp in componentsToReplace)
				{
					try
					{
						IntPtr compPtr = Marshal.GetIUnknownForObject(comp);
						if (!seenPointers.Contains(compPtr))
						{
							seenPointers.Add(compPtr);
							uniqueComponents.Add(comp);
							string compName = comp?.Name2 ?? "null";
							LogFileWriter.Write($"Leo AI :   - Unique component: {compName}");
						}
						else
						{
							LogFileWriter.Write($"Leo AI :   - Skipping duplicate component");
							Marshal.Release(compPtr);
						}
					}
					catch (Exception dupEx)
					{
						LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Error checking duplicate: {dupEx.Message}");
					}
				}
				componentsToReplace = uniqueComponents;

				// Store positions and transformations before removing
				List<ComponentPlacementInfo> placementInfos = new List<ComponentPlacementInfo>();
				for (int i = 0; i < componentsToReplace.Count; i++)
				{
					try
					{
						LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Getting placement info for component {i + 1}...");
						ComponentPlacementInfo info = GetComponentPlacementInfo(componentsToReplace[i]);
						placementInfos.Add(info);
					}
					catch (Exception infoEx)
					{
						LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - ERROR getting placement info for component {i + 1}: {infoEx.Message}");
					}
				}
				LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Stored {placementInfos.Count} placement info(s)");

				List<Component2> insertedComponents = new List<Component2>();
				for (int i = 0; i < placementInfos.Count; i++)
				{
					try
					{
						LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Inserting component {i + 1} of {placementInfos.Count}...");
						Component2 insertedComp = InsertComponentAtLocation(assemblyDoc, newPartFilePath, placementInfos[i]);
						if (insertedComp != null)
						{
							insertedComponents.Add(insertedComp);
							LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Component {i + 1} insertion completed successfully");
						}
					}
					catch (Exception insertEx)
					{
						LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - ERROR inserting component {i + 1}: {insertEx.Message}");
					}
				}
				LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Component insertion phase completed. Successfully inserted: {insertedComponents.Count} of {placementInfos.Count}");

				// Remove old components only if replaceExisting is true
				if (replaceExisting)
				{
					LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - ReplaceExisting is true, removing original components...");
					for (int i = 0; i < componentsToReplace.Count; i++)
					{
						try
						{
							LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - Removing component {i + 1} of {componentsToReplace.Count}...");
							RemoveComponent(assemblyDoc, componentsToReplace[i]);
						}
						catch (Exception removeEx)
						{
							LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - ERROR removing component {i + 1}: {removeEx.Message}");
						}
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : ReplacePartInAssembly - FATAL ERROR: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Finds components in assembly by name (first) or file path (fallback)
		/// </summary>
		/// <param name="assemblyDoc">The assembly document</param>
		/// <param name="componentTitle">Component name to search for</param>
		/// <param name="componentFilePath">Component file path to search for</param>
		/// <returns>List of matching components</returns>
		private List<Component2> FindComponentsToReplace(AssemblyDoc assemblyDoc, string componentTitle, string componentFilePath)
		{
			List<Component2> foundComponents = new List<Component2>();

			try
			{
				LogFileWriter.Write($"Leo AI : FindComponentsToReplace - START");
				LogFileWriter.Write($"Leo AI : FindComponentsToReplace - Title: {componentTitle ?? "null"}, FilePath: {componentFilePath ?? "null"}");

				// Get all components in the assembly (including root and all children)
				LogFileWriter.Write($"Leo AI : FindComponentsToReplace - Calling GetComponents(false)...");
				object[] allComponents = assemblyDoc.GetComponents(false) as object[];
				LogFileWriter.Write($"Leo AI : FindComponentsToReplace - GetComponents returned: {(allComponents == null ? "null" : $"{allComponents.Length} components")}");

				if (allComponents == null || allComponents.Length == 0)
				{
					LogFileWriter.Write($"Leo AI : FindComponentsToReplace - No components found in assembly.");
					return foundComponents;
				}

				// First, try to find by component title (name)
				if (!string.IsNullOrEmpty(componentTitle))
				{
					LogFileWriter.Write($"Leo AI : FindComponentsToReplace - Searching by title: {componentTitle}");
					int compIndex = 0;
					foreach (object compObj in allComponents)
					{
						try
						{
							compIndex++;
							Component2 comp = compObj as Component2;
							if (comp != null)
							{
								LogFileWriter.Write($"Leo AI : FindComponentsToReplace - Processing component {compIndex}/{allComponents.Length}");
								FindComponentsByTitle(comp, componentTitle, foundComponents);
							}
						}
						catch (Exception compEx)
						{
							LogFileWriter.Write($"Leo AI : FindComponentsToReplace - Error processing component {compIndex}: {compEx.Message}");
						}
					}
				}

				// If not found by title, search by file path
				if (foundComponents.Count == 0 && !string.IsNullOrEmpty(componentFilePath))
				{
					LogFileWriter.Write($"Leo AI : FindComponentsToReplace - Title search failed, searching by file path: {componentFilePath}");
					int compIndex = 0;
					foreach (object compObj in allComponents)
					{
						try
						{
							compIndex++;
							Component2 comp = compObj as Component2;
							if (comp != null)
							{
								LogFileWriter.Write($"Leo AI : FindComponentsToReplace - Processing component {compIndex}/{allComponents.Length} for file path search");
								FindComponentsByFilePath(comp, componentFilePath, foundComponents);
							}
						}
						catch (Exception compEx)
						{
							LogFileWriter.Write($"Leo AI : FindComponentsToReplace - Error processing component {compIndex} for file path: {compEx.Message}");
						}
					}
				}

			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : FindComponentsToReplace - FATAL ERROR: {ex.Message}");
			}

			return foundComponents;
		}

		/// <summary>
		/// Recursively finds components by title (name)
		/// </summary>
		private void FindComponentsByTitle(Component2 component, string targetTitle, List<Component2> results)
		{
			LogFileWriter.Write($"Leo AI : FindComponentsByTitle - start");
			if (component == null) return;
			LogFileWriter.Write($"Leo AI : FindComponentsByTitle - component is not null");
			try
			{
				// Check if this component matches
				string componentName = component.Name2;
				LogFileWriter.Write($"Leo AI : FindComponentsByTitle - Found component: {componentName}");
				if (!string.IsNullOrEmpty(componentName) && 
				    componentName.Equals(targetTitle, StringComparison.OrdinalIgnoreCase))
				{
					results.Add(component);
					LogFileWriter.Write($"Leo AI : FindComponentsByTitle - Found component: {componentName}");
				}

				// Recursively search child components
				object[] children = component.GetChildren() as object[];
				if (children != null)
				{
					foreach (object childObj in children)
					{
						Component2 childComp = childObj as Component2;
						if (childComp != null)
						{
							FindComponentsByTitle(childComp, targetTitle, results);
						}
					}
				}
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : FindComponentsByTitle Error - {ex.Message}");
			}
		}

		/// <summary>
		/// Recursively finds components by file path
		/// </summary>
		private void FindComponentsByFilePath(Component2 component, string targetFilePath, List<Component2> results)
		{
			LogFileWriter.Write($"Leo AI : FindComponentsByFilePath start");
			if (component == null) return;

			try
			{
				// Get the model document for this component
				ModelDoc2 compDoc = component.GetModelDoc2() as ModelDoc2;
				if (compDoc != null)
				{
					string compPath = compDoc.GetPathName();
					LogFileWriter.Write($"Leo AI : FindComponentsByFilePath - compPath: {compPath}");
					if (!string.IsNullOrEmpty(compPath))
					{
						// Compare file paths (case-insensitive, normalize paths)
						string normalizedCompPath = Path.GetFullPath(compPath);
						string normalizedTargetPath = Path.GetFullPath(targetFilePath);
						LogFileWriter.Write($"Leo AI : FindComponentsByFilePath - normalizedCompPath: {normalizedCompPath}");
						LogFileWriter.Write($"Leo AI : FindComponentsByFilePath - normalizedTargetPath: {normalizedTargetPath}");

						if (normalizedCompPath.Equals(normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
						{
							results.Add(component);
							LogFileWriter.Write($"Leo AI : FindComponentsByFilePath - Found component: {compPath}");
						}
					}
				}

				// Recursively search child components
				object[] children = component.GetChildren() as object[];
				if (children != null)
				{
					foreach (object childObj in children)
					{
						Component2 childComp = childObj as Component2;
						if (childComp != null)
						{
							FindComponentsByFilePath(childComp, targetFilePath, results);
						}
					}
				}
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : FindComponentsByFilePath Error - {ex.Message}");
			}
		}

		/// <summary>
		/// Stores component placement information (position, rotation, mates)
		/// </summary>
		private class ComponentPlacementInfo
		{
			public double[] Transform { get; set; }
			public bool IsFixed { get; set; }
			public string Name { get; set; }
		}

		/// <summary>
		/// Gets placement information from a component
		/// </summary>
		private ComponentPlacementInfo GetComponentPlacementInfo(Component2 component)
		{
			ComponentPlacementInfo info = new ComponentPlacementInfo();
			try
			{
				
				if (component == null)
				{
					return info;
				}

				// Get component name
				try
				{
					info.Name = component.Name2;
				}
				catch (Exception nameEx)
				{
					info.Name = "unknown";
				}

				// Get fixed state
				try
				{
					info.IsFixed = component.IsFixed();
				}
				catch (Exception fixedEx)
				{
					info.IsFixed = false;
				}
				
				// Get transformation matrix (16-element array: 4x4 matrix)
				try
				{
					MathTransform transform = component.Transform2;
					if (transform != null)
					{
						LogFileWriter.Write($"Leo AI : GetComponentPlacementInfo - Transform2 is not null");
						object arrayData = transform.ArrayData;
						if (arrayData != null)
						{
							info.Transform = arrayData as double[];
							if (info.Transform != null)
							{
								LogFileWriter.Write($"Leo AI : GetComponentPlacementInfo - Transform array length: {info.Transform.Length}");
								if (info.Transform.Length >= 16)
								{
									LogFileWriter.Write($"Leo AI : GetComponentPlacementInfo - Transform values: [{string.Join(", ", info.Transform.Take(16))}]");
								}
							}
						}
					}
				}
				catch (Exception transformEx)
				{
					LogFileWriter.Write($"Leo AI : GetComponentPlacementInfo - Error getting transform: {transformEx.Message}");
				}

				// Default identity matrix if no transform
				if (info.Transform == null || info.Transform.Length < 16)
				{
					info.Transform = new double[] {
						1, 0, 0, 0,
						0, 1, 0, 0,
						0, 0, 1, 0,
						0, 0, 0, 1
					};
				}

			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : GetComponentPlacementInfo - FATAL ERROR: {ex.Message}");
			}
			return info;
		}

		/// <summary>
		/// Removes a component from the assembly
		/// </summary>
		private void RemoveComponent(AssemblyDoc assemblyDoc, Component2 component)
		{
			try
			{
				LogFileWriter.Write($"Leo AI : RemoveComponent - START");
				
				if (component == null)
				{
					return;
				}

				string compName = "unknown";
				try
				{
					compName = component.Name2;
				}
				catch (Exception nameEx)
				{
					LogFileWriter.Write($"Leo AI : RemoveComponent - Error getting component name: {nameEx.Message}");
				}

				// Get the ModelDoc2 interface for EditDelete
				ModelDoc2 modelDoc = assemblyDoc as ModelDoc2;
				if (modelDoc == null)
				{
					return;
				}

				bool selected = false;
				try
				{
					selected = component.Select2(false, 0);
				}
				catch (Exception selectEx)
				{
					LogFileWriter.Write($"Leo AI : RemoveComponent - ERROR in Select2: {selectEx.Message}");
					return;
				}

				if (selected)
				{
					LogFileWriter.Write($"Leo AI : RemoveComponent - Component selected, calling EditDelete...");
					try
					{
						modelDoc.EditDelete();
						LogFileWriter.Write($"Leo AI : RemoveComponent - EditDelete completed successfully");
					}
					catch (Exception deleteEx)
					{
						LogFileWriter.Write($"Leo AI : RemoveComponent - ERROR in EditDelete: {deleteEx.Message}");
						throw;
					}
				}

			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : RemoveComponent - FATAL ERROR: {ex.Message}");
				throw;
			}
		}

		/// <summary>
		/// Inserts a new component at the specified location
		/// </summary>
		/// <returns>The inserted component, or null if insertion failed</returns>
		private Component2 InsertComponentAtLocation(AssemblyDoc assemblyDoc, string newPartFilePath, ComponentPlacementInfo placementInfo)
		{
			try
			{
				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - START");
				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - File path: {newPartFilePath ?? "null"}");
				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Placement info: Name={placementInfo?.Name ?? "null"}, IsFixed={placementInfo?.IsFixed ?? false}");

				if (placementInfo == null)
				{
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - ERROR: PlacementInfo is null");
					return null;
				}

				// Open the new part document if not already open
				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Checking if part is already open...");
				ModelDoc2 newPartDoc = null;
				try
				{
					newPartDoc = solidWorksApplication.GetOpenDocumentByName(newPartFilePath);
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - GetOpenDocumentByName returned: {(newPartDoc == null ? "null" : newPartDoc.GetTitle())}");
				}
				catch (Exception getDocEx)
				{
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Error in GetOpenDocumentByName: {getDocEx.Message}");
				}

				if (newPartDoc == null)
				{
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Part not open, opening now...");
					try
					{
						DocumentSpecification swDocSpecification = solidWorksApplication.GetOpenDocSpec(newPartFilePath);
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - GetOpenDocSpec completed");
						newPartDoc = solidWorksApplication.OpenDoc7(swDocSpecification);
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - OpenDoc7 returned: {(newPartDoc == null ? "null" : newPartDoc.GetTitle())}");
					}
					catch (Exception openEx)
					{
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - ERROR opening document: {openEx.Message}");
						return null;
					}

					if (newPartDoc == null)
					{
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - ERROR: Failed to open new part: {newPartFilePath}");
						return null;
					}
				}

				// Activate the assembly
				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Activating assembly...");
				ModelDoc2 assemblyModel = assemblyDoc as ModelDoc2;
				if (assemblyModel != null)
				{
					try
					{
						string assemblyTitle = assemblyModel.GetTitle();
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Assembly title: {assemblyTitle}");
						solidWorksApplication.ActivateDoc3(assemblyTitle, false, 
							(int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Assembly activated");
					}
					catch (Exception activateEx)
					{
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - ERROR activating assembly: {activateEx.Message}");
					}
				}
				else
				{
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - WARNING: Could not cast to ModelDoc2");
				}

				// Extract position from transformation matrix
				// SolidWorks MathTransform.ArrayData format: 16-element array representing 4x4 matrix
				// Based on the log, the format appears to be: [r11, r12, r13, 0, r21, r22, r23, 0, r31, r32, r33, 0, tx, ty, tz, 1]
				// OR: [r11, r12, r13, tx, r21, r22, r23, ty, r31, r32, r33, tz, 0, 0, 0, 1]
				// Looking at actual data: [1, 0, 0, 0, 1, 0, 0, 0, 1, -0.068476037414632, 0.02, 0, 1, 0, 0, 0]
				// This suggests: Translation is at indices 9, 10, 11 (after 3x3 rotation matrix at 0-8)
				double x = 0, y = 0, z = 0;
				if (placementInfo.Transform != null && placementInfo.Transform.Length >= 16)
				{
					// Format based on actual data: Translation at indices 9, 10, 11
					x = placementInfo.Transform[9];
					y = placementInfo.Transform[10];
					z = placementInfo.Transform[11];
					
				}

				// Verify file exists before insertion
				if (!File.Exists(newPartFilePath))
				{
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - ERROR: File does not exist: {newPartFilePath}");
					return null;
				}
				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - File exists, proceeding with insertion");

				// Insert component at the same location
				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Calling AddComponent4 with path: {newPartFilePath}, config: '', position: ({x}, {y}, {z})");
				Component2 insertedComp = null;
				try
				{
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - About to call AddComponent4...");
					insertedComp = assemblyDoc.AddComponent4(newPartFilePath, "", x, y, z) as Component2;
					
					if (insertedComp != null)
					{
						try
						{
							string insertedName = insertedComp.Name2;
							LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - AddComponent4 SUCCESS - Component name: {insertedName}");
						}
						catch (Exception nameEx)
						{
							LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - AddComponent4 returned component but error getting name: {nameEx.Message}");
						}
					}
					else
					{
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - AddComponent4 returned NULL");
						
						// Try to get error information
						ModelDoc2 asmModel = assemblyDoc as ModelDoc2;
						if (asmModel != null)
						{
							try
							{
								// GetFeatureCount takes only 1 argument (includeHidden)
								int featureCount = asmModel.GetFeatureCount();
							}
							catch (Exception featEx)
							{
								LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Error getting feature count: {featEx.Message}");
							}
						}
						
						// Try alternative: use the opened document reference instead of file path
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Attempting alternative insertion method...");
						if (newPartDoc != null)
						{
							try
							{
								string partPath = newPartDoc.GetPathName();
								LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Trying with document path: {partPath}");
								insertedComp = assemblyDoc.AddComponent4(partPath, "", x, y, z) as Component2;
								if (insertedComp != null)
								{
									LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Alternative method SUCCESS");
								}
							}
							catch (Exception altEx)
							{
								LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Alternative method error: {altEx.Message}");
							}
						}
					}
				}
				catch (Exception addCompEx)
				{
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - CRASH in AddComponent4: {addCompEx.Message}");
					throw;
				}

				if (insertedComp != null)
				{
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Successfully inserted component at ({x}, {y}, {z})");

					// If original component was fixed, fix the new one too
					if (placementInfo.IsFixed)
					{
						// Select the component first, then fix it
						bool selected = false;
						try
						{
							selected = insertedComp.Select2(false, 0);
							LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Select2 for fixing returned: {selected}");
						}
						catch (Exception selectEx)
						{
							LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - ERROR in Select2 for fixing: {selectEx.Message}");
						}

						if (selected)
						{
							try
							{
								// Use FixComponent method from AssemblyDoc to fix the selected component
								assemblyDoc.FixComponent();
								LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - FixComponent completed");
							}
							catch (Exception fixEx)
							{
								LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - ERROR in FixComponent: {fixEx.Message}");
							}
						}
						else
						{
							LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Failed to select component for fixing");
						}
					}

					// Close the opened part document
					LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Closing part document...");
					try
					{
						string partTitle = newPartDoc.GetTitle();
						solidWorksApplication.CloseDoc(partTitle);
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - Part document closed: {partTitle}");
					}
					catch (Exception closeEx)
					{
						LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - ERROR closing document: {closeEx.Message}");
					}
				}

				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - END");
				return insertedComp;
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : InsertComponentAtLocation - FATAL ERROR: {ex.Message}");
				return null;
			}
		}
	}
}
