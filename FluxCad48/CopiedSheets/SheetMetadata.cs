using System.Collections.Generic;

namespace FluxCad48.CopiedSheets
{
	public sealed class SheetMetadata
	{
		public string Material { get; set; }
		public int? Quantity { get; set; }

		public string QuantityText { get; set; }
		public string ExtraQuantityText { get; set; }

		public double? Thickness { get; set; }
		public string ThicknessSource { get; set; }

		public int? HoleCount { get; set; }
		public int? BendCount { get; set; }

		public double? HoleSizeSum { get; set; }
		public double? EstimatedFlatWidth { get; set; }
		public double? EstimatedFlatHeight { get; set; }

		public List<string> RawTexts { get; private set; }
		public List<string> Warnings { get; private set; }

		public SheetMetadata()
		{
			Material = "";
			QuantityText = "";
			ExtraQuantityText = "";
			ThicknessSource = "";

			RawTexts = new List<string>();
			Warnings = new List<string>();
		}
	}
}