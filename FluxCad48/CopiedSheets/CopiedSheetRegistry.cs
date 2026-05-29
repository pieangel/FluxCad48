using FluxCad48.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;

namespace FluxCad48.CopiedSheets
{
	public static class CopiedSheetRegistry
	{
		private static readonly Dictionary<string, CopiedSheetRecord> _items
			= new Dictionary<string, CopiedSheetRecord>();

		public static void Register(CopiedSheetRecord info)
		{
			if (info == null)
				return;

			if (string.IsNullOrWhiteSpace(info.SheetCode))
				return;

			_items[info.SheetCode] = info;
		}

		public static bool TryGet(string sheetCode, out CopiedSheetRecord info)
		{
			info = null;

			if (string.IsNullOrWhiteSpace(sheetCode))
				return false;

			return _items.TryGetValue(sheetCode.Trim(), out info);
		}

		public static List<CopiedSheetRecord> GetAll()
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

		public static CopiedSheetRecord FindByEntityId(ObjectId entityId)
		{
			foreach (CopiedSheetRecord sheet in _items.Values)
			{
				if (sheet.CopiedEntityIds.Contains(entityId))
					return sheet;
			}

			return null;
		}
	}
}