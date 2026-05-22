using FluxCad48.Sheets;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace FluxCad48.Brics
{
	public static class FluxXDataTools
	{
		public const string AppName = "FLUXCAD";

		public static void EnsureRegApp(Transaction tr, Database db)
		{
			RegAppTable table =
				(RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);

			if (table.Has(AppName))
				return;

			table.UpgradeOpen();

			RegAppTableRecord record = new RegAppTableRecord();
			record.Name = AppName;

			table.Add(record);
			tr.AddNewlyCreatedDBObject(record, true);
		}

		public static void SetCopiedSheetEntityXData(
			Entity entity,
			CopiedSheetInfo info)
		{
			ResultBuffer rb = new ResultBuffer(
				new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "ROLE=CopiedSheetEntity"),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "COPY_GROUP_ID=" + info.CopyGroupId),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "SOURCE_KIND=" + info.SourceKind),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "SOURCE_FRAME_HANDLE=" + info.SourceFrameHandle),
				new TypedValue((int)DxfCode.ExtendedDataInteger32, info.Generation)
			);

			entity.XData = rb;
		}

		public static void SetCopiedSheetMarkerXData(
			Entity marker,
			CopiedSheetInfo info)
		{
			ResultBuffer rb = new ResultBuffer(
				new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "ROLE=CopiedSheetMarker"),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "COPY_GROUP_ID=" + info.CopyGroupId),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "SOURCE_KIND=" + info.SourceKind),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "SOURCE_FRAME_HANDLE=" + info.SourceFrameHandle),
				new TypedValue((int)DxfCode.ExtendedDataAsciiString, "GENERATION=" + info.Generation)
			);

			marker.XData = rb;
		}
	}
}