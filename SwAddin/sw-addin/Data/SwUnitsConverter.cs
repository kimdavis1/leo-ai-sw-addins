using System;

namespace sw_addin.Data
{
	public static class SwUnitsConverter
	{
		private static string _currentDocUnits;

		public static string CurrentDocUnits
		{
			get => _currentDocUnits;
			set
			{
				_currentDocUnits = value;
				SetConversionFactor();//set the conversion factor based on current doc units
			}
		}

		private static double ConversionFactor { get; set; }
		private static string NewUnit { get; set; }

		/// <summary>
		/// Sets the required conversion factor based on Document units
		/// </summary>
		/// <returns></returns>
		private static double SetConversionFactor()
		{
			switch (_currentDocUnits)
			{
				case "cm":
					ConversionFactor = 1000;
					NewUnit = "mm";
					break;
				case "m":
					ConversionFactor = 1;
					NewUnit = "m";
					break;
				case "mm":
					ConversionFactor = 1000;
					NewUnit = "mm";
					break;
				case "in":
					ConversionFactor = 1 / 0.0254;
					NewUnit = "in";
					break;

				default:
					ConversionFactor = 1000;
					NewUnit = "mm";
					break;
			}
			return ConversionFactor;
		}

		/// <summary>
		/// Converts the provided value based on conversion factor
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		public static string ConvertAndFormat(double value)
		{
			if (value != -1)
			{
				double convertedValue = value * ConversionFactor;
				return $"{convertedValue:F6}{NewUnit}";
			}
			return null;
		}

		/// <summary>
		/// Converts Radians to Degrees
		/// </summary>
		/// <param name="radians"></param>
		/// <returns></returns>
		public static string RadiansToDegree(double radians) 
		{
			return (radians * (180 / Math.PI)).ToString()  + "Degrees";
		}
	}
}
