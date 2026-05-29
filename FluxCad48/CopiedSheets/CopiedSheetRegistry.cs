using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCad48.CopiedSheets
{
	public static class CopiedSheetRegistry
	{
		private static readonly Dictionary<string, CopiedSheetInfo> _items
			= new Dictionary<string, CopiedSheetInfo>();

		public static void Register(CopiedSheetInfo info)
		{
			if (info == null)
				return;

			if (string.IsNullOrWhiteSpace(info.SheetCode))
				return;

			_items[info.SheetCode] = info;
		}

		public static bool TryGet(string sheetCode, out CopiedSheetInfo info)
		{
			info = null;

			if (string.IsNullOrWhiteSpace(sheetCode))
				return false;

			return _items.TryGetValue(sheetCode.Trim(), out info);
		}

		public static List<CopiedSheetInfo> GetAll()
		{
			return _items.Values
				.OrderBy(x => x.SheetCode)
				.ToList();
		}

		public static void Clear()
		{
			_items.Clear();
		}

		public static int Count
		{
			get { return _items.Count; }
		}
	}
}