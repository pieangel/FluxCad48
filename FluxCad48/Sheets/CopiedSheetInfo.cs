using System;
using System.Collections.Generic;
using FluxCad48.Geometry;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.Sheets
{
	public sealed class CopiedSheetInfo
	{
		public string CopyGroupId { get; set; } = "";
		public string SourceKind { get; set; } = "OriginalFrame";
		public string SourceFrameHandle { get; set; } = "";

		public Bounds2D SourceFrameBounds { get; set; }
		public Bounds2D CopiedFrameBounds { get; set; }

		public Vector3d PlacementOffset { get; set; }

		public int Generation { get; set; } = 1;
		public DateTime CreatedAt { get; set; } = DateTime.Now;

		public List<ObjectId> CopiedEntityIds { get; } = new List<ObjectId>();
		public ObjectId MarkerId { get; set; } = ObjectId.Null;
	}
}