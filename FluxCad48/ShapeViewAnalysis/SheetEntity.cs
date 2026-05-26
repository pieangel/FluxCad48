using System;
using System.Collections.Generic;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public sealed class SheetEntity
	{
		public string Handle { get; set; }
		public SheetEntityKind Kind { get; set; }

		public string EntityType { get; set; }
		public string Layer { get; set; }
		public string BlockName { get; set; }

		public Bounds2D Bounds { get; set; }
		public Point2D Anchor { get; set; }

		public string Text { get; set; }
		public string TextNormalized { get; set; }

		public double RotationDeg { get; set; }
		public double TextHeight { get; set; }

		public double ScaleX { get; set; }
		public double ScaleY { get; set; }

		public List<string> BlockPath { get; private set; }
		public int Depth { get; set; }

		// BlockReference 추적용
		public bool IsFromBlock { get; set; }
		public bool IsWorldCoordinate { get; set; }
		public string SourceBlockName { get; set; }
		public string ParentHandle { get; set; }

		// 형상 분석용 최소 기하 정보
		public Point2D? StartPoint { get; set; }
		public Point2D? EndPoint { get; set; }

		public List<Point2D> Vertices { get; private set; }
		public bool IsClosed { get; set; }

		public Point2D? CenterPoint { get; set; }
		public double? Radius { get; set; }

		public double? StartAngleDeg2D { get; set; }
		public double? EndAngleDeg2D { get; set; }

		public double? MajorRadius { get; set; }
		public double? MinorRadius { get; set; }
		public double? EllipseRotationDeg2D { get; set; }

		// 의미 힌트
		public string LinetypeName { get; set; }
		public string EffectiveLinetypeName { get; set; }

		public bool IsCenterLine { get; set; }
		public bool IsHiddenLine { get; set; }
		public bool IsReferenceLine { get; set; }

		public bool IsVisible { get; set; }

		// 시각 속성
		public int? ColorIndex { get; set; }
		public string ColorName { get; set; }
		public int? LineWeight { get; set; }
		public byte? TransparencyAlpha { get; set; }

		public SheetEntity()
		{
			Handle = "";
			Kind = SheetEntityKind.Unknown;

			EntityType = "";
			Layer = "";
			BlockName = "";

			Text = "";
			TextNormalized = "";

			ScaleX = 1.0;
			ScaleY = 1.0;

			BlockPath = new List<string>();
			Vertices = new List<Point2D>();

			IsFromBlock = false;
			IsWorldCoordinate = true;
			SourceBlockName = "";
			ParentHandle = "";

			LinetypeName = "";
			EffectiveLinetypeName = "";
			ColorName = "";

			IsVisible = true;
		}

		public bool HasText
		{
			get
			{
				return !string.IsNullOrWhiteSpace(TextNormalized)
					|| !string.IsNullOrWhiteSpace(Text);
			}
		}

		public bool IsBlockReference
		{
			get { return Kind == SheetEntityKind.BlockReference; }
		}

		public bool IsTextLike
		{
			get
			{
				return Kind == SheetEntityKind.Text
					|| Kind == SheetEntityKind.MText
					|| Kind == SheetEntityKind.InsertAttribute;
			}
		}

		public bool IsDimensionLike
		{
			get
			{
				return Kind == SheetEntityKind.Dimension
					|| Kind == SheetEntityKind.Leader;
			}
		}

		public bool IsGeometryLike
		{
			get
			{
				return Kind == SheetEntityKind.Line
					|| Kind == SheetEntityKind.Polyline
					|| Kind == SheetEntityKind.Arc
					|| Kind == SheetEntityKind.Circle
					|| Kind == SheetEntityKind.Ellipse
					|| Kind == SheetEntityKind.Spline
					|| Kind == SheetEntityKind.Hatch
					|| Kind == SheetEntityKind.Solid
					|| Kind == SheetEntityKind.Face
					|| Kind == SheetEntityKind.Region
					|| Kind == SheetEntityKind.Point;
			}
		}

		public bool IsReferenceGeometry
		{
			get
			{
				return IsCenterLine
					|| IsHiddenLine
					|| IsReferenceLine;
			}
		}

		public bool IsUsableVisibleGeometry
		{
			get
			{
				return IsGeometryLike
					&& IsVisible
					&& !IsReferenceGeometry;
			}
		}

		public bool HasGeometryPoints
		{
			get
			{
				return StartPoint.HasValue
					|| EndPoint.HasValue
					|| CenterPoint.HasValue
					|| (Vertices != null && Vertices.Count > 0);
			}
		}

		public string EntityTypeName
		{
			get { return EntityType ?? ""; }
		}

		public Point2D RepresentativePoint
		{
			get { return Anchor; }
		}

		public void AddBlockPath(string blockName)
		{
			if (string.IsNullOrWhiteSpace(blockName))
				return;

			BlockPath.Add(blockName);
			Depth = BlockPath.Count;
			IsFromBlock = true;
		}

		public string GetBlockPathText()
		{
			if (BlockPath == null || BlockPath.Count == 0)
				return "";

			return string.Join("/", BlockPath.ToArray());
		}
	}
}