using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Robust.Shared.Maths;

namespace Robust.Client.UserInterface.Controls
{
    /// <summary>
    ///     A container that lays out its children in a grid. Can define specific count of
    ///     rows or specific count of columns (not both), and will grow to fill in additional rows/columns within
    ///     that limit. Alternatively, can define a maximum width or height, and grid will
    ///     lay out elements (aligned in a grid pattern, not floated) within the defined limit.
    /// </summary>
    [Virtual]
    public class GridContainer : Container
    {
        // limit - depending on mode, this is either rows or columns
        private int _limitedDimensionCount = 1;
        // virtual pixels
        private float _limitSize;
        private LimitType _limitType = LimitType.Count;
        private Dimension _limitDimension = Dimension.Column;


        /// <summary>
        /// Indicates whether row or column count has been specified, and thus
        /// how items will fill them out as they are added.
        /// This is set depending on whether you have specified Columns or Rows.
        /// </summary>
        public Dimension LimitedDimension => _limitDimension;
        /// <summary>
        /// Opposite dimension of LimitedDimension
        /// </summary>
        public Dimension UnlimitedDimension => _limitDimension == Dimension.Column ? Dimension.Row : Dimension.Column;

        /// <summary>
        /// Indicates whether we are limiting based on an explicit number of rows or columns, or limiting
        /// based on a defined max width or height.
        /// </summary>
        public LimitType LimitType => _limitType;

        /// <summary>
        ///     Controls the direction in which the grid grows once the limited dimension is full.
        ///     By default new content continues to the right (row-limited grid) or downward (column-limited grid).
        ///     When true, growth goes the opposite way: the first row/column is placed at the far end and the
        ///     following ones are laid out backwards from it.
        ///     Setting this always invalidates the arrangement, even when the value did not change.
        /// </summary>
        public bool ExpandBackwards
        {
            get => _expandBackwards;
            set
            {
                _expandBackwards = value;
                InvalidateArrange();
            }
        }
        private bool _expandBackwards;

        /// <summary>
        ///     The number of columns to organize the children into. Setting this puts this grid
        ///     into LimitMode.LimitColumns and LimitType.Count - items will be added to fill up the entire row, up to the defined
        ///     limit of columns, and then create a second row.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown if the value assigned is less than or equal to 0.
        /// </exception>
        /// <returns>specified limit if LimitMode.LimitColums, otherwise the number
        /// of columns being used for the current amount of children.</returns>
        public int Columns
        {
            get => GetCount(Dimension.Column);
            set => SetCount(Dimension.Column, value);
        }

        /// <summary>
        ///     The number of rows to organize the children into. Setting this puts this grid
        ///     into LimitMode.LimitRows and LimitType.Count - items will be added to fill up the entire column, up to the defined
        ///     limit of rows, and then create a second column.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown if the value assigned is less than or equal to 0.
        /// </exception>
        /// <returns>specified limit if LimitMode.LimitRows, otherwise the number
        /// of rows being used for the current number of children.</returns>
        public int Rows
        {
            get => GetCount(Dimension.Row);
            set => SetCount(Dimension.Row, value);
        }

        /// <summary>
        ///     The max width (in virtual pixels) the grid of elements can have. This dynamically determines
        ///     the number of columns based on the size of the elements. Setting this puts this grid
        ///     into LimitMode.LimitColumns and LimitType.Size. Items will be added to fill up the entire row, up to the defined
        ///     width, and then create a second row.
        ///
        ///     In the presence of unevenly-sized children,
        ///     rows will still have the same amount elements - the items are laid out in a grid pattern such
        ///     that they are all aligned, the height and width of each "cell" being determined by
        ///     the greatest min height and min width among the elements. In the presence of expanding elements,
        ///     their pre-expanded size will be used to determine the cell layout, then the elements expand within
        ///     the defined Control.Size
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown if the value assigned is less than or equal to 0.
        /// </exception>
        public float MaxGridWidth
        {
            set => SetMaxSize(Dimension.Column, value);
        }

        /// <summary>
        ///     The max height (in virtual pixels) the grid  of elements can have. This dynamically determines
        ///     the number of rows based on the size of the elements. Setting this puts this grid
        ///     into LimitMode.LimitRows and LimitType.Size - items will be added to fill up the entire column, up to the defined
        ///     height, and then create a second column.
        ///
        ///     In the presence of unevenly-sized children,
        ///     columns will still have the same amount elements - the items are laid out in a grid pattern such
        ///     that they are all aligned, the height and width of each "cell" being determined by
        ///     the greatest min height and min width among the elements. In the presence of expanding elements,
        ///     their pre-expanded size will be used to determine the layout, then the elements expand within
        ///     the defined Control.Size
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown if the value assigned is less than or equal to 0.
        /// </exception>
        public float MaxGridHeight
        {
            set => SetMaxSize(Dimension.Row, value);
        }

        private int? _vSeparationOverride;

        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        public int? VSeparationOverride
        {
            get => _vSeparationOverride;
            set => _vSeparationOverride = value;
        }

        private int? _hSeparationOverride;

        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        public int? HSeparationOverride
        {
            get => _hSeparationOverride;
            set => _hSeparationOverride = value;
        }

        private Vector2i Separations => (_hSeparationOverride ?? 4, _vSeparationOverride ?? 4);

        private float GetLimitPixelSize()
        {
            return _limitSize * UIScale;
        }

        private int GetCount(Dimension forDimension)
        {
            if (_limitType == LimitType.Count)
            {
                if (forDimension == _limitDimension) return _limitedDimensionCount;
                if (ChildCount == 0)
                {
                    return 1;
                }

                var divisor = (_limitDimension == Dimension.Column ? Columns : Rows);
                var div = ChildCount / divisor;
                if (ChildCount % divisor != 0)
                {
                    div += 1;
                }

                return div;
            }
            else
            {
                if (forDimension == _limitDimension) return CalculateLimitedCount();
                if (ChildCount == 0)
                {
                    return 1;
                }
                var divisor = CalculateLimitedCount();;
                var div = ChildCount / divisor;
                if (ChildCount % divisor != 0)
                {
                    div += 1;
                }

                return div;
            }
        }

        private void SetCount(Dimension forDimension, int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be greater than zero.");
            }

            _limitDimension = forDimension;
            _limitType = LimitType.Count;

            _limitedDimensionCount = value;
            InvalidateMeasure();
        }

        private void SetMaxSize(Dimension forDimension, float value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be greater than zero.");
            }

            _limitDimension = forDimension;
            _limitType = LimitType.Size;

            _limitSize = value;
            InvalidateMeasure();
        }

        /// <summary>
        /// If columns (or width) are being limited, calculates how many columns
        /// there should be.
        /// </summary>
        private int CalculateLimitedCount()
        {
            if (_limitType == LimitType.Count) return _limitedDimensionCount;

            // to make it easier to read and visualize, we're just going to use the terms "x" and "y", width, and height,
            // rows and cols,
            // but at the start of the method here we'll set those to what they actually are based
            // on the limited dimension, which might involve swapping them.
            // For the below convention, we pretend that columns (or width) have a limit defined, thus
            // the amount of rows is not limited (unlimited).

            // we convert all elements to "cells" of the same size so they will align,
            // also converting to our pretend scenario of limited columns/width
            var (cellWidthActual, cellHeightActual) = CellSize();
            var (wSepActual, hSepActual) = (Vector2i) (Separations * UIScale);
            var cellWidth = _limitDimension == Dimension.Column ? cellWidthActual : cellHeightActual;
            var wSep = _limitDimension == Dimension.Column ? wSepActual : hSepActual;

            // calculate how many cells will fit into a given column without going over, accounting
            // for additional wSep between each cell only if there's more than one
            if (ChildCount == 0) return 1;

            if ((2 * cellWidth + wSep) > GetLimitPixelSize())
            {
                return 1;
            }

            return Math.Min(ChildCount, (int) ((GetLimitPixelSize() + wSep) / (cellWidth + wSep)));
        }

        /// <summary>
        /// Calculates the size of a "cell" in physical pixels, for use in LimitType.Size mode. This
        /// is based on the maximum minheight / minwidth of each element.
        /// </summary>
        /// <returns></returns>
        private Vector2i CellSize()
        {
            int maxMinWidth = -1;
            int maxMinHeight = -1;
            foreach (var child in Children)
            {
                var (minSizeX, minSizeY) = child.DesiredPixelSize;
                maxMinWidth = Math.Max(maxMinWidth, minSizeX);
                maxMinHeight = Math.Max(maxMinHeight, minSizeY);
            }

            return new Vector2i(maxMinWidth, maxMinHeight);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            // to make it easier to read and visualize, we're just going to use the terms "x" and "y", width, and height,
            // rows and cols,
            // but at the start of the method here we'll set those to what they actually are based
            // on the limited dimension, which might involve swapping them.
            // For the below convention, we pretend that columns have a limit defined, thus
            // the amount of rows is not limited (unlimited).

            foreach (var child in Children)
            {
                // TODO: This is not really correct in any fucking way but I CBA to fix this properly.
                child.Measure(availableSize);
            }

            var rows = GetCount(UnlimitedDimension);
            var cols = GetCount(LimitedDimension);
            var cellSize = CellSize();

            Span<int> minColWidth = stackalloc int[cols];
            Span<int> minRowHeight = stackalloc int[rows];

            minColWidth.Fill(0);
            minRowHeight.Fill(0);

            var index = 0;
            foreach (var child in Children)
            {
                if (!child.Visible)
                {
                    continue;
                }

                var column = index % cols;
                var row = index / cols;

                // also converting here to our "pretend" scenario where columns have a limit defined.
                // note if we are limiting by size rather than count, the size of each child is constant (cell size)
                var (minSizeXActual, minSizeYActual) = _limitType == LimitType.Count ? child.DesiredPixelSize : cellSize;
                var minSizeX = _limitDimension == Dimension.Column ? minSizeXActual : minSizeYActual;
                var minSizeY = _limitDimension == Dimension.Column ? minSizeYActual : minSizeXActual;
                minColWidth[column] = Math.Max(minSizeX, minColWidth[column]);
                minRowHeight[row] = Math.Max(minSizeY, minRowHeight[row]);

                index += 1;
            }

            // converting here to our "pretend" scenario where columns have a limit defined
            var (wSepActual, hSepActual) = (Vector2i) (Separations * UIScale);
            var wSep = _limitDimension == Dimension.Column ? wSepActual : hSepActual;
            var hSep = _limitDimension == Dimension.Column ? hSepActual : wSepActual;
            var minWidth = AccumSizes(minColWidth, wSep);
            var minHeight = AccumSizes(minRowHeight, hSep);

            // converting back from our pretend scenario where columns are limited
            return new Vector2(
                _limitDimension == Dimension.Column ? minWidth : minHeight,
                _limitDimension == Dimension.Column ? minHeight : minWidth) / UIScale;
        }

        private static int AccumSizes(Span<int> sizes, int separator)
        {
            var totalSize = 0;
            var first = true;

            foreach (var size in sizes)
            {
                totalSize += size;

                if (first)
                {
                    first = false;
                }
                else
                {
                    totalSize += separator;
                }
            }

            return totalSize;
        }

        /// <summary>
        ///     Places every visible child into its grid cell (in pixel space) and returns <paramref name="finalSize"/>.
        /// </summary>
        /// <remarks>
        ///     All math is done in a "pretend" frame where columns are the limited dimension. When the real
        ///     limited dimension is the row, width/height roles are swapped on the way in and swapped back
        ///     when each child's box is built.
        /// </remarks>
        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var limCol = _limitDimension == Dimension.Column;

            var cols = GetCount(LimitedDimension);
            var rows = GetCount(UnlimitedDimension);

            // Phase 1: per-column / per-row minimum sizes and expand flags.
            Span<int> minColWidth = stackalloc int[cols];
            Span<int> minRowHeight = stackalloc int[rows];
            Span<bool> colExpand = stackalloc bool[cols];
            Span<bool> rowExpand = stackalloc bool[rows];

            minColWidth.Fill(0);
            minRowHeight.Fill(0);
            colExpand.Fill(false);
            rowExpand.Fill(false);

            var cellSize = CellSize();

            var index = 0;
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;

                var column = index % cols;
                var row = index / cols;

                var (realMinX, realMinY) = _limitType == LimitType.Count ? child.DesiredPixelSize : cellSize;
                var minX = limCol ? realMinX : realMinY;
                var minY = limCol ? realMinY : realMinX;

                minColWidth[column] = Math.Max(minColWidth[column], minX);
                minRowHeight[row] = Math.Max(minRowHeight[row], minY);

                var expandForColumn = limCol ? child.HorizontalExpand : child.VerticalExpand;
                var expandForRow = UnlimitedDimension == Dimension.Column
                    ? child.HorizontalExpand
                    : child.VerticalExpand;

                colExpand[column] |= expandForColumn;
                rowExpand[row] |= expandForRow;

                index += 1;
            }

            // Phase 2: spare space is split equally between the expanding columns / rows.
            var stretchMinX = 0;
            var stretchCountX = 0;
            for (var i = 0; i < cols; i++)
            {
                if (colExpand[i])
                    stretchCountX += 1;
                else
                    stretchMinX += minColWidth[i];
            }

            var stretchMinY = 0;
            var stretchCountY = 0;
            for (var i = 0; i < rows; i++)
            {
                if (rowExpand[i])
                    stretchCountY += 1;
                else
                    stretchMinY += minRowHeight[i];
            }

            // Note: the component pairing below (column gap uses Y when column-limited) is intentional
            // legacy behavior and is kept as is.
            var (sepX, sepY) = (Vector2i) (Separations * UIScale);
            var hSep = limCol ? sepY : sepX;
            var vSep = limCol ? sepX : sepY;

            var width = (limCol ? finalSize.X : finalSize.Y) * UIScale;
            var height = (limCol ? finalSize.Y : finalSize.X) * UIScale;

            var stretchMaxX = width - hSep * (cols - 1);
            var stretchMaxY = height - vSep * (rows - 1);

            var stretchAvailX = Math.Max(0, stretchMaxX - stretchMinX);
            var stretchAvailY = Math.Max(0, stretchMaxY - stretchMinY);

            for (var i = 0; i < cols; i++)
            {
                if (colExpand[i])
                    minColWidth[i] = (int) (stretchAvailX / stretchCountX);
            }

            for (var i = 0; i < rows; i++)
            {
                if (rowExpand[i])
                    minRowHeight[i] = (int) (stretchAvailY / stretchCountY);
            }

            // Phase 3: when growing backwards, find the vertical position of the last row.
            var finalVOffset = 0;
            if (ExpandBackwards)
            {
                index = 0;
                for (var i = 0; i < ChildCount; i++)
                {
                    var child = GetChild(i);
                    var column = index % cols;
                    var row = index / cols;
                    index += 1;

                    if (!child.Visible)
                    {
                        index -= 1;
                        continue;
                    }

                    if (column == 0 && row != 0)
                        finalVOffset += vSep + minRowHeight[row - 1];
                }
            }

            // Phase 4: place the children.
            var hOffset = 0;
            var vOffset = ExpandBackwards ? finalVOffset : 0;
            index = 0;

            for (var i = 0; i < ChildCount; i++)
            {
                var child = GetChild(i);
                if (!child.Visible)
                    continue;

                var column = index % cols;
                var row = index / cols;
                index += 1;

                if (column == 0)
                {
                    hOffset = 0;

                    if (row != 0)
                    {
                        var step = vSep + minRowHeight[row - 1];
                        if (ExpandBackwards)
                            vOffset -= step;
                        else
                            vOffset += step;
                    }
                }

                var left = limCol ? hOffset : vOffset;
                var top = limCol ? vOffset : hOffset;
                var boxWidth = limCol ? minColWidth[column] : minRowHeight[row];
                var boxHeight = limCol ? minRowHeight[row] : minColWidth[column];

                child.ArrangePixel(UIBox2i.FromDimensions(left, top, boxWidth, boxHeight));

                hOffset += minColWidth[column] + hSep;
            }

            return finalSize;
        }
    }

    public enum Dimension : byte
    {
        Column,
        Row
    }

    public enum LimitType : byte
    {
        /// <summary>
        /// Defined number of rows or columns
        /// </summary>
        Count,
        /// <summary>
        /// Defined max width or height, inside of which the number of rows or columns
        /// will be fit.
        /// </summary>
        Size
    }
}
