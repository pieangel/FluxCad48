using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering
{
	public class OccupancyCell2d
	{
		public int Row { get; set; }

		public int Col { get; set; }

		public Point2d Min { get; set; }

		public Point2d Max { get; set; }

		public bool Occupied { get; set; }

		public bool BorderConnected { get; set; }

		public int IslandId { get; set; }

		public OccupancyCell2d()
		{
			Row = 0;
			Col = 0;
			Min = new Point2d();
			Max = new Point2d();
			Occupied = false;
			BorderConnected = false;
			IslandId = -1;
		}

		public Point2d Center
		{
			get
			{
				return new Point2d(
					(Min.X + Max.X) * 0.5,
					(Min.Y + Max.Y) * 0.5);
			}
		}

		public double Width
		{
			get
			{
				return Max.X - Min.X;
			}
		}

		public double Height
		{
			get
			{
				return Max.Y - Min.Y;
			}
		}

		public Extents3d Bounds
		{
			get
			{
				return new Extents3d(
					new Point3d(Min.X, Min.Y, 0),
					new Point3d(Max.X, Max.Y, 0));
			}
		}
	}
}