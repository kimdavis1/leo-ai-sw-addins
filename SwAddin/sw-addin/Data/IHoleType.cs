using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;


namespace sw_addin.Data
{
	public interface IHoleType
	{
		string Name { get; set; }
		void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData);
	}


	public class SimpleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData) 
		{
		}

	}


	public class TaperedHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData) 
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("MajorDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.MajorDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("MinorDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.MinorDiameter)));
		}

	}

	public class CounterBoredHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData) 
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterBoreDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterBoreDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterBoreDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterBoreDiameter)));
		}	
	}

	public class CounterSunkHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterSinkAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.CounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterSinkDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("FarCounterSinkAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.FarCounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("FarCounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.FarCounterSinkDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("MidCounterSinkAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.MidCounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("MidCounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.MidCounterSinkDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("NearCounterSinkAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.NearCounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("NearCounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.NearCounterSinkDiameter)));
		}
	}

	public class CounterDrilledHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterDrillAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.CounterDrillAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("FarCounterSinkAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.FarCounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("FarCounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.FarCounterSinkDiameter)));
		}
	}

	public class SimpleDrilledHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("DrillAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.DrillAngle)));
		}
	}

	public class TaperedDrilledHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("DrillAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.DrillAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("MajorDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.MajorDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("MinorDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.MinorDiameter)));
		}
	}

public class CounterBoredDrilledHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterBoreDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterBoreDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterBoreDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterBoreDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterDrillDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("DrillAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.DrillAngle)));
		}
	}

	public class CounterSunkDrilledHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterSinkAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.CounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterSinkDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("DrillAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.DrillAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("FarCounterSinkAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.FarCounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("FarCounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.FarCounterSinkDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("MidCounterSinkAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.MidCounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("MidCounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.MidCounterSinkDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("NearCounterSinkAngle", SwUnitsConverter.ConvertAndFormat(holeWizardData.NearCounterSinkAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("NearCounterSinkDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.NearCounterSinkDiameter)));
		}
	}

	public class CounterDrilledDrilledHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterDrillAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.CounterDrillAngle)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("CounterDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.CounterDrillDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("DrillAngle", SwUnitsConverter.RadiansToDegree(holeWizardData.DrillAngle)));
		}
	}

	public class CounterBoreBlindHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreBlindCounterSinkMiddleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreBlindCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreBlindCounterSinkTopMiddleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreThruCounterSinkMiddleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreThruCounterSinkMiddleBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreThruCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreThruCounterSinkTopBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreThruCounterSinkTopMiddleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreThruCounterSinkTopMiddleBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class HoleBlindHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class HoleBlindCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkBlindHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class HoleThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class HoleThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class HoleThruCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class HoleThruCounterSinkTopBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class TapBlindHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDiameter)));
		}
	}

	public class TapBlindCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDiameter)));
		}
	}

	public class TapThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDepth ", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDiameter)));
		}
	}

	public class TapThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDepth ", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDiameter ", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDiameter)));
		}
	}

	public class TapThruCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDiameter)));
		}
	}

	public class TapThruCounterSinkTopBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("TapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.TapDrillDiameter)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDepth", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDepth)));
			holeInfo.HoleSpecificInfo.Add(new KeyValuePair<string, string>("ThruTapDrillDiameter", SwUnitsConverter.ConvertAndFormat(holeWizardData.ThruTapDrillDiameter)));
		}
	}

	public class PipeTapBlindHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class PipeTapBlindCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class PipeTapThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class PipeTapThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class PipeTapThruCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class PipeTapThruCounterSinkTopBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}



	public class CounterSinkBlindWithoutHeadClearanceHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}



	public class CounterSinkThruWithoutHeadClearanceHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}


	public class CounterSinkThruCounterSinkBottomWithoutHeadClearanceHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class TapBlindCosmeticThreadHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapBlindCosmeticThreadCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapThruCosmeticThreadHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapThruCosmeticThreadCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapThruCosmeticThreadCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapThruCosmeticThreadCounterSinkTopBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapThruThreadThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapThruThreadThruCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapThruThreadThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapThruThreadThruCountersinkTopBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class TapBlindRemoveThreadHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			swWzdHoleCosmeticThreadTypes_e swWzdHoleCosmeticThreadTypes_E = (swWzdHoleCosmeticThreadTypes_e)holeWizardData.CosmeticThreadType;
			holeInfo.CosmeticThreadType = swWzdHoleCosmeticThreadTypes_E.ToString();
		}
	}

	public class CounterBoreSlotBlindHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotBlindCounterSinkMiddleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotBlindCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotBlindCounterSinkTopMiddleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotThruCounterSinkMiddleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotThruCounterSinkMiddleBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotThruCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotThruCounterSinkTopBottomHole : IHoleType
	{
		string IHoleType.Name {get; set;}

		void IHoleType.GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotThruCounterSinkTopMiddleHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterBoreSlotThruCounterSinkTopMiddleBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}	


	public class SlotBlindHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class SlotBlindCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkSlotBlindHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class SlotThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class SlotThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class SlotThruCounterSinkTopHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class SlotThruCounterSinkTopBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkSlotThruHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkSlotThruCounterSinkBottomHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkSlotBlindWithoutHeadClearanceHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkSlotThruWithoutHeadClearanceHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}

	public class CounterSinkSlotThruCounterSinkBottomWithoutHeadClearanceHole : IHoleType
	{
		public string Name {get; set;}

		public void GetHoleProperties(HoleInfo holeInfo, WizardHoleFeatureData2 holeWizardData)
		{
			
		}
	}


	public class HoleInfo
	{		
		public string Diameter { get; set; }
		public string Depth { get; set; }		
		public string ThreadAngle { get; internal set; }
		public string HoleType { get; set; }

		/// <summary>
		/// cosmetic thread for this tap or pipe-tap Hole Wizard feature
		/// </summary>
		public string CosmeticThreadType { get; set; }

		public List<HoleElementData> HoleElementsInfo { get; set; }
		
		public string EndCondition { get; internal set; }
		public string FastenerSize { get; internal set; }
		public string FastenerType { get; internal set; }
		public string HeadClearance { get; internal set; }
		public string HeadClearanceType { get; internal set; }
		public string SlotLength { get; internal set; }
		public string OffSetDistance { get; internal set; }
		public bool ReverseDirection { get; internal set; }
		public string Standard { get; internal set; }
		public string TapType { get; internal set; }
		public string ThreadDepth { get; internal set; }
		public string ThredDiameter { get; internal set; }
		public string ThreadEndCondition { get; internal set; }
		public string ThruHoleDepth { get; internal set; }
		public string ThruHoleDiameter { get; internal set; }

		public List<KeyValuePair<string, string>> HoleSpecificInfo = new List<KeyValuePair<string, string>>();
		
	}

	/// <summary>
	/// Hole factorty based on hole type..
	/// </summary>
	public class HoleTypeFactory
	{
		public static IHoleType CreateHoleType(swWzdHoleTypes_e holeType)
		{
			switch (holeType)
			{
				case swWzdHoleTypes_e.swSimple:
					return new SimpleHole();

				case swWzdHoleTypes_e.swTapered:
					return new TaperedHole();

				case swWzdHoleTypes_e.swCounterBored:
					return new CounterBoredHole();

				case swWzdHoleTypes_e.swCounterSunk:
					return new CounterSunkHole();

				case swWzdHoleTypes_e.swCounterDrilled:
					return new CounterDrilledHole();

				case swWzdHoleTypes_e.swSimpleDrilled:
					return new SimpleDrilledHole();

				case swWzdHoleTypes_e.swTaperedDrilled:
					return new TaperedDrilledHole();

				case swWzdHoleTypes_e.swCounterBoredDrilled:
					return new CounterBoredDrilledHole();

				case swWzdHoleTypes_e.swCounterSunkDrilled:
					return new CounterSunkDrilledHole();

				case swWzdHoleTypes_e.swCounterDrilledDrilled:
					return new CounterDrilledDrilledHole();

				case swWzdHoleTypes_e.swCounterBoreBlind:
					return new CounterBoreBlindHole();

				case swWzdHoleTypes_e.swCounterBoreBlindCounterSinkMiddle:
					return new CounterBoreBlindCounterSinkMiddleHole();

				case swWzdHoleTypes_e.swCounterBoreBlindCounterSinkTop:
					return new CounterBoreBlindCounterSinkTopHole();

				case swWzdHoleTypes_e.swCounterBoreBlindCounterSinkTopmiddle:
					return new CounterBoreBlindCounterSinkTopMiddleHole();

				case swWzdHoleTypes_e.swCounterBoreThru:
					return new CounterBoreThruHole();

				case swWzdHoleTypes_e.swCounterBoreThruCounterSinkBottom:
					return new CounterBoreThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swCounterBoreThruCounterSinkMiddle:
					return new CounterBoreThruCounterSinkMiddleHole();

				case swWzdHoleTypes_e.swCounterBoreThruCounterSinkMiddleBottom:
					return new CounterBoreThruCounterSinkMiddleBottomHole();

				case swWzdHoleTypes_e.swCounterBoreThruCounterSinkTop:
					return new CounterBoreThruCounterSinkTopHole();

				case swWzdHoleTypes_e.swCounterBoreThruCounterSinkTopBottom:
					return new CounterBoreThruCounterSinkTopBottomHole();

				case swWzdHoleTypes_e.swCounterBoreThruCounterSinkTopMiddle:
					return new CounterBoreThruCounterSinkTopMiddleHole();

				case swWzdHoleTypes_e.swCounterBoreThruCounterSinkTopMiddleBottom:
					return new CounterBoreThruCounterSinkTopMiddleBottomHole();

				case swWzdHoleTypes_e.swHoleBlind:
					return new HoleBlindHole();

				case swWzdHoleTypes_e.swHoleBlindCounterSinkTop:
					return new HoleBlindCounterSinkTopHole();

				case swWzdHoleTypes_e.swCounterSinkBlind:
					return new CounterSinkBlindHole();

				case swWzdHoleTypes_e.swHoleThru:
					return new HoleThruHole();

				case swWzdHoleTypes_e.swHoleThruCounterSinkBottom:
					return new HoleThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swHoleThruCounterSinkTop:
					return new HoleThruCounterSinkTopHole();

				case swWzdHoleTypes_e.swHoleThruCounterSinkTopBottom:
					return new HoleThruCounterSinkTopBottomHole();

				case swWzdHoleTypes_e.swCounterSinkThru:
					return new CounterSinkThruHole();

				case swWzdHoleTypes_e.swCounterSinkThruCounterSinkBottom:
					return new CounterSinkThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swTapBlind:
					return new TapBlindHole();

				case swWzdHoleTypes_e.swTapBlindCounterSinkTop:
					return new TapBlindCounterSinkTopHole();

				case swWzdHoleTypes_e.swTapThru:
					return new TapThruHole();

				case swWzdHoleTypes_e.swTapThruCounterSinkBottom:
					return new TapThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swTapThruCounterSinkTop:
					return new TapThruCounterSinkTopHole();

				case swWzdHoleTypes_e.swTapThruCounterSinkTopBottom:
					return new TapThruCounterSinkTopBottomHole();

				case swWzdHoleTypes_e.swPipeTapBlind:
					return new PipeTapBlindHole();

				case swWzdHoleTypes_e.swPipeTapBlindCounterSinkTop:
					return new PipeTapBlindCounterSinkTopHole();

				case swWzdHoleTypes_e.swPipeTapThru:
					return new PipeTapThruHole();

				case swWzdHoleTypes_e.swPipeTapThruCounterSinkBottom:
					return new PipeTapThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swPipeTapThruCounterSinkTop:
					return new PipeTapThruCounterSinkTopHole();

				case swWzdHoleTypes_e.swPipeTapThruCounterSinkTopBottom:
					return new PipeTapThruCounterSinkTopBottomHole();

				case swWzdHoleTypes_e.swCounterSinkBlindWithoutHeadClearance:
					return new CounterSinkBlindWithoutHeadClearanceHole();

				case swWzdHoleTypes_e.swCounterSinkThruWithoutHeadClearance:
					return new CounterSinkThruWithoutHeadClearanceHole();

				case swWzdHoleTypes_e.swCounterSinkThruCounterSinkBottomWithoutHeadClearance:
					return new CounterSinkThruCounterSinkBottomWithoutHeadClearanceHole();

				case swWzdHoleTypes_e.swTapBlindCosmeticThread:
					return new TapBlindCosmeticThreadHole();

				case swWzdHoleTypes_e.swTapBlindCosmeticThreadCounterSinkTop:
					return new TapBlindCosmeticThreadCounterSinkTopHole();

				case swWzdHoleTypes_e.swTapThruCosmeticThread:
					return new TapThruCosmeticThreadHole();

				case swWzdHoleTypes_e.swTapThruCosmeticThreadCounterSinkTop:
					return new TapThruCosmeticThreadCounterSinkTopHole();

				case swWzdHoleTypes_e.swTapThruCosmeticThreadCounterSinkBottom:
					return new TapThruCosmeticThreadCounterSinkBottomHole();

				case swWzdHoleTypes_e.swTapThruCosmeticThreadCounterSinkTopBottom:
					return new TapThruCosmeticThreadCounterSinkTopBottomHole();

				case swWzdHoleTypes_e.swTapThruThreadThru:
					return new TapThruThreadThruHole();

				case swWzdHoleTypes_e.swTapThruThreadThruCounterSinkTop:
					return new TapThruThreadThruCounterSinkTopHole();

				case swWzdHoleTypes_e.swTapThruThreadThruCounterSinkBottom:
					return new TapThruThreadThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swTapThruThreadThruCountersinkTopBottom:
					return new TapThruThreadThruCountersinkTopBottomHole();

				case swWzdHoleTypes_e.swTapBlindRemoveThread:
					return new TapBlindRemoveThreadHole();

				case swWzdHoleTypes_e.swCounterBoreSlotBlind:
					return new CounterBoreSlotBlindHole();

				case swWzdHoleTypes_e.swCounterBoreSlotBlindCounterSinkMiddle:
					return new CounterBoreSlotBlindCounterSinkMiddleHole();

				case swWzdHoleTypes_e.swCounterBoreSlotBlindCounterSinkTop:
					return new CounterBoreSlotBlindCounterSinkTopHole();

				case swWzdHoleTypes_e.swCounterBoreSlotBlindCounterSinkTopMiddle:
					return new CounterBoreSlotBlindCounterSinkTopMiddleHole();

				case swWzdHoleTypes_e.swCounterBoreSlotThru:
					return new CounterBoreSlotThruHole();

				case swWzdHoleTypes_e.swCounterBoreSlotThruCounterSinkBottom:
					return new CounterBoreSlotThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swCounterBoreSlotThruCounterSinkMiddle:
					return new CounterBoreSlotThruCounterSinkMiddleHole();

				case swWzdHoleTypes_e.swCounterBoreSlotThruCounterSinkMiddleBottom:
					return new CounterBoreSlotThruCounterSinkMiddleBottomHole();

				case swWzdHoleTypes_e.swCounterBoreSlotThruCounterSinkTop:
					return new CounterBoreSlotThruCounterSinkTopHole();

				case swWzdHoleTypes_e.swCounterBoreSlotThruCounterSinkTopBottom:
					return new CounterBoreSlotThruCounterSinkTopBottomHole();

				case swWzdHoleTypes_e.swCounterBoreSlotThruCounterSinkTopMiddle:
					return new CounterBoreSlotThruCounterSinkTopMiddleHole();

				case swWzdHoleTypes_e.swCounterBoreSlotThruCounterSinkTopMiddleBottom:
					return new CounterBoreSlotThruCounterSinkTopMiddleBottomHole();

				case swWzdHoleTypes_e.swSlotBlind:
					return new SlotBlindHole();

				case swWzdHoleTypes_e.swSlotBlindCounterSinkTop:
					return new SlotBlindCounterSinkTopHole();

				case swWzdHoleTypes_e.swCounterSinkSlotBlind:
					return new CounterSinkSlotBlindHole();

				case swWzdHoleTypes_e.swSlotThru:
					return new SlotThruHole();

				case swWzdHoleTypes_e.swSlotThruCounterSinkBottom:
					return new SlotThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swSlotThruCounterSinkTop:
					return new SlotThruCounterSinkTopHole();

				case swWzdHoleTypes_e.swSlotThruCounterSinkTopBottom:
					return new SlotThruCounterSinkTopBottomHole();

				case swWzdHoleTypes_e.swCounterSinkSlotThru:
					return new CounterSinkSlotThruHole();

				case swWzdHoleTypes_e.swCounterSinkSlotThruCounterSinkBottom:
					return new CounterSinkSlotThruCounterSinkBottomHole();

				case swWzdHoleTypes_e.swCounterSinkSlotBlindWithoutHeadClearance:
					return new CounterSinkSlotBlindWithoutHeadClearanceHole();

				case swWzdHoleTypes_e.swCounterSinkSlotThruWithoutHeadClearance:
					return new CounterSinkSlotThruWithoutHeadClearanceHole();

				case swWzdHoleTypes_e.swCounterSinkSlotThruCounterSinkBottomWithoutHeadClearance:
					return new CounterSinkSlotThruCounterSinkBottomWithoutHeadClearanceHole();

				default:
					throw new ArgumentOutOfRangeException(nameof(holeType), "Invalid hole type");
			}
		}
	}

}

