using System.Collections.Generic;

namespace sw_addin.Data
{
	public class SwMeasurementData
	{
		public string Area { get; set; }
		public string Perimeter { get; set; }
		public string Radius { get; set; }
		public string Diameter { get; set; }
		public Point3D CenterPoint { get; set; }
		public string Normal { get; set; }
		public bool IsHole { get; set; }
		public string surfaceType { get; set; }
		public HoleInfo SelectedHoleInfo { get; set; }
		// Add more properties as needed
	}

	public class Point3D
	{
		public string X { get; set; }
		public string Y { get; set; }
		public string Z { get; set; }
	}

	public class HoleData
	{
		public string ThreadSize { get; set; }
		public string HoleDiameter { get; set; }
		public string HoleDepth { get; set; }
		public string Standard { get;  set; }
		public string ThreadClass { get;  set; }
		public string ThreadDepth { get; set; }
		public string ThreadDiameter { get;	set; }
		public string ThreadAngle { get; internal set; }
		public string HoleType { get; set; }

		public List<HoleElementData> HoleElementsInfo { get; set; }
		public string TapDrillDiameter { get; internal set; }
		public string FastenerType { get; internal set; }
		public string MajorDiameter { get; internal set; }
		public string Length { get; internal set; }
		public string ScrewClearacneType { get; internal set; }
		public string HeadClearance { get; internal set; }
		public string FastenerSize { get; internal set; }
	}

	public class HoleElementData
	{
		public string BlindDepth { get; set; }
		public string Size { get; set; }
		public string Diameter { get; set; }
		public string ElementType { get; set; }
		public string OffsetDistance { get; set; }
		public string HoleStandard { get; set; }
		public string FastenerType { get; set; }
		public bool IsNearest { get; set; }
	}
}
