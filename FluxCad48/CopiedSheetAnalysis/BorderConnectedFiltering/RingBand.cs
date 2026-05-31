using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering
{
	public class RingBand
	{
		public int RingIndex { get; set; }

		public Extents3d OuterBounds { get; set; }

		public Extents3d InnerBounds { get; set; }

		public double Thickness { get; set; }

		public bool ContainsPoint(Point3d p)
		{
			bool inOuter =
				p.X >= OuterBounds.MinPoint.X &&
				p.X <= OuterBounds.MaxPoint.X &&
				p.Y >= OuterBounds.MinPoint.Y &&
				p.Y <= OuterBounds.MaxPoint.Y;

			bool inInner =
				p.X >= InnerBounds.MinPoint.X &&
				p.X <= InnerBounds.MaxPoint.X &&
				p.Y >= InnerBounds.MinPoint.Y &&
				p.Y <= InnerBounds.MaxPoint.Y;

			return inOuter && !inInner;
		}

		public bool Intersects(Extents3d bounds)
		{
			if (!IntersectsBounds(OuterBounds, bounds))
				return false;

			if (ContainsFully(InnerBounds, bounds))
				return false;

			return true;
		}

		private static bool IntersectsBounds(Extents3d a, Extents3d b)
		{
			if (a.MaxPoint.X < b.MinPoint.X) return false;
			if (a.MinPoint.X > b.MaxPoint.X) return false;
			if (a.MaxPoint.Y < b.MinPoint.Y) return false;
			if (a.MinPoint.Y > b.MaxPoint.Y) return false;

			return true;
		}

		private static bool ContainsFully(Extents3d outer, Extents3d inner)
		{
			return
				inner.MinPoint.X >= outer.MinPoint.X &&
				inner.MaxPoint.X <= outer.MaxPoint.X &&
				inner.MinPoint.Y >= outer.MinPoint.Y &&
				inner.MaxPoint.Y <= outer.MaxPoint.Y;
		}
	}
}