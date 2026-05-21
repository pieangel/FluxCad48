namespace FluxCad48.Geometry
{
	public class CadPoint2D
	{
		public double X { get; set; }
		public double Y { get; set; }

		public CadPoint2D()
		{
		}

		public CadPoint2D(double x, double y)
		{
			X = x;
			Y = y;
		}
	}
}