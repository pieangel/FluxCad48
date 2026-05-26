using System.Collections.Generic;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public sealed class ClosedLoopInfo
	{
		public int LoopIndex { get; set; }

		public List<SheetEntity> Entities { get; private set; }

		public Bounds2D Bounds { get; set; }

		public double Area { get; set; }

		public bool IsOuterLoop { get; set; }

		public bool IsValid { get; set; }

		public string FailReason { get; set; }

		public ClosedLoopInfo()
		{
			Entities = new List<SheetEntity>();
			FailReason = "";
		}
	}
}