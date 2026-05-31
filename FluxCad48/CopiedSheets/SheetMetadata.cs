using System.Collections.Generic;

namespace FluxCad48.CopiedSheets
{
	public enum QuantityState
	{
		None,
		Exact,
		Empty,
		FromSet,
		Ambiguous,
		Conflict
	}

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

		public QuantityState QuantityState { get; set; }

		public string DisplayQuantity
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(QuantityText))
					return QuantityText;

				if (Quantity.HasValue)
					return Quantity.Value.ToString();

				return "없음";
			}
		}

		public string DisplayMaterial
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(Material))
					return Material;

				return "없음";
			}
		}

		public string ToDisplayText()
		{
			return "재질 : " + DisplayMaterial + ", 수량 : " + DisplayQuantity;
		}

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