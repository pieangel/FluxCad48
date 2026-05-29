using System;
using System.Collections.Generic;
using FluxCad48.Geometry;
using FluxCad48.ShapeViewAnalysis;
using Teigha.DatabaseServices;

namespace FluxCad48.CopiedSheets
{
	public sealed class CopiedSheetInfo
	{
		public string SheetCode { get; set; }

		public Bounds2D SourceBounds { get; set; }
		public Bounds2D CopiedBounds { get; set; }

		public double MoveX { get; set; }
		public double MoveY { get; set; }

		public ObjectId SourceFrameObjectId { get; set; }
		public ObjectId CopiedFrameObjectId { get; set; }
		public ObjectId SourceLabelObjectId { get; set; }
		public ObjectId CopiedLabelObjectId { get; set; }

		public List<ObjectId> SourceEntityIds { get; private set; }
		public List<ObjectId> CopiedEntityIds { get; private set; }

		public Dictionary<ObjectId, ObjectId> SourceToCopiedMap { get; private set; }

		public List<SheetEntity> SourceEntities { get; private set; }
		public List<SheetEntity> CopiedEntities { get; private set; }

		public SheetMetadata Metadata { get; set; }

		public CopiedSheetInfo()
		{
			SheetCode = "";

			SourceEntityIds = new List<ObjectId>();
			CopiedEntityIds = new List<ObjectId>();

			SourceToCopiedMap = new Dictionary<ObjectId, ObjectId>();

			SourceEntities = new List<SheetEntity>();
			CopiedEntities = new List<SheetEntity>();

			Metadata = new SheetMetadata();
		}

		public bool IsValid
		{
			get
			{
				return !string.IsNullOrWhiteSpace(SheetCode)
					&& CopiedBounds != null
					&& CopiedBounds.IsValid;
			}
		}

		public void AddCopiedEntity(ObjectId sourceId, ObjectId copiedId)
		{
			if (!sourceId.IsNull && !SourceEntityIds.Contains(sourceId))
				SourceEntityIds.Add(sourceId);

			if (!copiedId.IsNull && !CopiedEntityIds.Contains(copiedId))
				CopiedEntityIds.Add(copiedId);

			if (!sourceId.IsNull && !copiedId.IsNull)
				SourceToCopiedMap[sourceId] = copiedId;
		}
	}
}