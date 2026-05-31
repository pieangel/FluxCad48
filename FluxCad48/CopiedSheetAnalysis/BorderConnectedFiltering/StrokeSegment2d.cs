using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering
{
	public class StrokeSegment2d
	{
		public ObjectId SourceEntityId { get; set; }

		public Point2d Start { get; set; }

		public Point2d End { get; set; }

		public string SourceType { get; set; }

		public StrokeSegment2d()
		{
			SourceEntityId = ObjectId.Null;
			Start = new Point2d();
			End = new Point2d();
			SourceType = "";
		}

		public StrokeSegment2d(
			ObjectId sourceEntityId,
			Point2d start,
			Point2d end,
			string sourceType)
		{
			SourceEntityId = sourceEntityId;
			Start = start;
			End = end;
			SourceType = sourceType ?? "";
		}

		public double Length
		{
			get
			{
				return Start.GetDistanceTo(End);
			}
		}
	}
}