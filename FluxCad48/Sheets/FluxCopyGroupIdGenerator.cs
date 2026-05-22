using System;

namespace FluxCad48.Sheets
{
	public static class FluxCopyGroupIdGenerator
	{
		private static int _seq = 0;

		public static string NewSheetCopyGroupId()
		{
			_seq++;
			return "SHEET_" +
				   DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
				   "_" +
				   _seq.ToString("0000");
		}
	}
}