using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using System;

namespace SwLeoAIAddin
{
	/// <summary>
	/// Mode for Property Manager Page - determines what type of selection is needed
	/// </summary>
	public enum PMPMode
	{
		FindComponent,  // For finding components - requires face selection
		PartReplacer    // For part replacement - requires component/part selection
	}

	public class UserPMPage
	{
		//Local Objects
		public IPropertyManagerPage2 swPropertyPage = null;
		PMPHandler handler = null;
		ISldWorks iSwApp = null;
		SwAddin userAddin = null;
		//IPropertyManagerPageTab ppagetab1 = null;
		//IPropertyManagerPageTab ppagetab2 = null;

		#region Property Manager Page Controls
		//Groups
		IPropertyManagerPageGroup group1;
		IPropertyManagerPageGroup group2;

		//Controls
		IPropertyManagerPageTextbox textbox1;
		IPropertyManagerPageCheckbox checkbox1;
		IPropertyManagerPageOption option1;
		IPropertyManagerPageOption option2;
		IPropertyManagerPageOption option3;
		IPropertyManagerPageListbox list1;

		IPropertyManagerPageSelectionbox selection1;
		IPropertyManagerPageNumberbox num1;
		IPropertyManagerPageCombobox combo1;

		IPropertyManagerPageButton button1;
		IPropertyManagerPageButton button2;
		public IPropertyManagerPageTextbox textbox2;
		public IPropertyManagerPageTextbox textbox3;

		// Mode to determine behavior
		public PMPMode Mode { get; private set; }

		//Control IDs
		public const int group1ID = 0;
		public const int group2ID = 1;

		public const int textbox1ID = 2;
		public const int checkbox1ID = 3;
		public const int option1ID = 4;
		public const int option2ID = 5;
		public const int option3ID = 6;
		public const int list1ID = 7;

		public const int selection1ID = 8;
		public const int num1ID = 9;
		public const int combo1ID = 10;
		//public const int tabID1 = 11;
		//public const int tabID2 = 12;
		//public const int buttonID1 = 13;
		//public const int buttonID2 = 14;
		//public const int textbox2ID = 15;
		//public const int textbox3ID = 16;
		#endregion

		public UserPMPage(SwAddin addin, PMPMode mode = PMPMode.FindComponent)
		{
			userAddin = addin;
			Mode = mode;
			if (userAddin != null)
			{
				iSwApp = (ISldWorks)userAddin.SwApp;
				CreatePropertyManagerPage();
			}
			else
			{
				System.Windows.Forms.MessageBox.Show("SwAddin not set.");
			}
		}


		protected void CreatePropertyManagerPage()
		{
			int errors = -1;
			int options = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
					(int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;

			handler = new PMPHandler(userAddin, this, Mode);
			swPropertyPage = (IPropertyManagerPage2)iSwApp.CreatePropertyManagerPage("Leo AI Copilot", options, handler, ref errors);
			if (swPropertyPage != null && errors == (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
			{
				try
				{
					AddControls();
				}
				catch (Exception e)
				{
					iSwApp.SendMsgToUser2(e.Message, 0, 0);
				}
			}
		}


		//Controls are displayed on the page top to bottom in the order 
		//in which they are added to the object.
		protected void AddControls()
		{
			short controlType = -1;
			short align = -1;
			int options = -1;
			bool retval;

			// Configure UI based on mode
			if (Mode == PMPMode.FindComponent)
			{
				//Add Message for Find Component
				retval = swPropertyPage.SetMessage3("Select a face to search for a compatible component with Leo. " + "\n" +
					" For instance, choose a hole face to look for a fitting screw.",
																				(int)swPropertyManagerPageMessageVisibility.swImportantMessageBox,
																				(int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand,
																				"Leo AI - Find Component.");

				////Add the groups
				options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible | (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

				group1 = (IPropertyManagerPageGroup)swPropertyPage.AddGroupBox(group1ID, "Selected Faces", options);

				//Add controls to group1
				//selection1
				controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
				align = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
				options = (int)swAddControlOptions_e.swControlOptions_Enabled |
									(int)swAddControlOptions_e.swControlOptions_Visible;

				selection1 = (IPropertyManagerPageSelectionbox)group1.AddControl(selection1ID, controlType, "Face Selection", align, options, "Displays features selected in main view");
				if (selection1 != null)
				{
					int[] filter = { (int)swSelectType_e.swSelFACES, (int)swSelectType_e.swSelEDGES };
					selection1.Height = 40;
					selection1.SetSelectionFilters(filter);
					selection1.SingleEntityOnly = true;//Allow only one entity selection
				}
			}
			else if (Mode == PMPMode.PartReplacer)
			{
				//Add Message for Part Replacer
				retval = swPropertyPage.SetMessage3("Select a part or component to replace. " + "\n" +
					" You can select a component in the assembly or a part document.",
																				(int)swPropertyManagerPageMessageVisibility.swImportantMessageBox,
																				(int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand,
																				"Leo AI - Part Replacer.");

				////Add the groups
				options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible | (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

				group1 = (IPropertyManagerPageGroup)swPropertyPage.AddGroupBox(group1ID, "Selected Part/Component", options);

				//Add controls to group1
				//selection1
				controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
				align = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
				options = (int)swAddControlOptions_e.swControlOptions_Enabled |
									(int)swAddControlOptions_e.swControlOptions_Visible;

				selection1 = (IPropertyManagerPageSelectionbox)group1.AddControl(selection1ID, controlType, "Part/Component Selection", align, options, "Displays part or component selected in main view");
				if (selection1 != null)
				{
					// Allow selection of components, faces (to get component), and parts
					int[] filter = { (int)swSelectType_e.swSelCOMPONENTS, (int)swSelectType_e.swSelFACES };
					selection1.Height = 40;
					selection1.SetSelectionFilters(filter);
					selection1.SingleEntityOnly = true;//Allow only one entity selection
				}
			}
		}

		public void Show()
		{
			if (swPropertyPage != null)
			{
				swPropertyPage.Show();
			}
		}
	}
}
