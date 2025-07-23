namespace SW_AddInTests
{
	public class Tests
	{

		private sw_addin.SwHelper _swHelper { get; set; } = null!;
		bool isSuccess = true;


		[SetUp]
		public void Setup()
		{
			_swHelper = new sw_addin.SwHelper();
		}

		[Test(Description = "CheckElectronAppRunning")]
		public void IsElectronAppRunningTest()
		{
			//Assign
			//string leoAppExe = @"C:\Program Files\Leo\Leo.exe";
			//Act
			bool isuccess = _swHelper.IsElectronAppRunning();
			//Assert
			Assert.IsTrue(isuccess);
		}

		[Test(Description = "OpenElectronApp")]
		public void OpenElectronAppTest()
		{
			//Assign
			//string leoAppExe = @"C:\Program Files\Leo\Leo.exe";
			//Act
			Task t =  _swHelper.OpenElectronApp("Leo is starting...");

			bool isuccess = _swHelper.IsElectronAppRunning();
			
			//Assert
			Assert.IsTrue(isuccess);
		}

		[Test(Description = "IsSolidworksFile")]
		public void IsSolidworksFileTest()
		{
			//Assign
			string swFilePath = @"C:\Program Files\test.SLPRT";
			//Act	

			bool isuccess = _swHelper.IsSolidWorksFile(swFilePath);

			//Assert
			Assert.IsTrue(isuccess);
		}


	}
}