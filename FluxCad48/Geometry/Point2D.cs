namespace FluxCad48.Geometry
{
	public readonly struct Point2D
	{
		public double X { get; }
		public double Y { get; }

		public Point2D(double x, double y)
		{
			X = x;
			Y = y;
		}

		public override string ToString()
		{
			return $"({X:0.###}, {Y:0.###})";
		}
	}
}