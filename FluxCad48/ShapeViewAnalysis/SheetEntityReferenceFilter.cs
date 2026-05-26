using System;

namespace FluxCad48.ShapeViewAnalysis
{
	public static class SheetEntityReferenceFilter
	{
		public static bool IsReferenceEntity(SheetEntity e)
		{
			if (e == null)
				return true;

			return IsReferenceLayer(e.Layer);
		}

		public static bool IsReferenceLayer(string layer)
		{
			if (string.IsNullOrWhiteSpace(layer))
				return false;

			string s = layer.Trim().ToUpperInvariant();

			if (s.Contains("CEN")) return true;
			if (s.Contains("CENTER")) return true;
			if (s.Contains("HID")) return true;
			if (s.Contains("HIDDEN")) return true;
			if (s.Contains("PHANTOM")) return true;
			if (s.Contains("DEFPOINTS")) return true;

			return false;
		}
	}
}