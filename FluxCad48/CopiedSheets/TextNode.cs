using FluxCad48.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCad48.CopiedSheets
{
	public sealed class TextNode
	{
		public string Text;
		public Bounds2D Bounds;

		public double CenterX
		{
			get { return (Bounds.MinX + Bounds.MaxX) * 0.5; }
		}

		public double CenterY
		{
			get { return (Bounds.MinY + Bounds.MaxY) * 0.5; }
		}
	}
}
