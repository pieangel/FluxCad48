namespace FluxCad48.Geometry
{
	public class Bounds2D
	{
		public double MinX { get; set; }
		public double MinY { get; set; }
		public double MaxX { get; set; }
		public double MaxY { get; set; }

		public double Width
		{
			get { return MaxX - MinX; }
		}

		public double Height
		{
			get { return MaxY - MinY; }
		}

		public double CenterX
		{
			get { return (MinX + MaxX) / 2.0; }
		}

		public double CenterY
		{
			get { return (MinY + MaxY) / 2.0; }
		}

		public bool IsValid
		{
			get { return MaxX > MinX && MaxY > MinY; }
		}

		public Bounds2D()
		{
		}

		public Bounds2D(double minX, double minY, double maxX, double maxY)
		{
			MinX = minX;
			MinY = minY;
			MaxX = maxX;
			MaxY = maxY;
		}

		public void ExpandToInclude(Bounds2D other)
		{
			if (other == null || !other.IsValid)
				return;

			if (!IsValid)
			{
				MinX = other.MinX;
				MinY = other.MinY;
				MaxX = other.MaxX;
				MaxY = other.MaxY;
				return;
			}

			MinX = System.Math.Min(MinX, other.MinX);
			MinY = System.Math.Min(MinY, other.MinY);
			MaxX = System.Math.Max(MaxX, other.MaxX);
			MaxY = System.Math.Max(MaxY, other.MaxY);
		}
	}
}