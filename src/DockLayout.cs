using System;

namespace Polka {
    // Geometry is independent of the desktop window so monitor sizes, DPI-scaled
    // work areas and the full shortcut capacity can be checked without a UI.
    internal sealed class DockLayout {
        internal int Rows, Columns, Lines;
        internal double IconSize, TileWidth, LabelHeight, CellWidth, CellHeight, GridWidth, GridHeight, Width, Height;
        internal bool Vertical;

        internal static DockLayout Create(int count,int preferredLines,double iconSize,double zoom,bool labels,bool vertical,double availableWidth,double availableHeight) {
            count=Math.Max(0,Math.Min(30,count));
            preferredLines=Math.Min(Math.Max(1,preferredLines),Math.Max(1,count));
            availableWidth=Math.Max(1,availableWidth); availableHeight=Math.Max(1,availableHeight);
            iconSize=Math.Max(1,iconSize); zoom=Math.Max(1,zoom);
            // Try the user's size first. Extra lines are preferable to making a
            // large collection of shortcuts progressively smaller.
            DockLayout result=Find(count,preferredLines,iconSize,zoom,labels,vertical,availableWidth,availableHeight);
            if(result!=null) return result;
            double lower=1,upper=iconSize;
            for(int iteration=0;iteration<36;iteration++) {
                double middle=(lower+upper)/2;
                if(Find(count,preferredLines,middle,zoom,labels,vertical,availableWidth,availableHeight)!=null) lower=middle;
                else upper=middle;
            }
            result=Find(count,preferredLines,lower,zoom,labels,vertical,availableWidth,availableHeight);
            if(result!=null) return result;
            // Only unusually tiny work areas reach this limit. Keep finite window
            // bounds even if the OS cannot offer room for the controls themselves.
            result=Measure(count,preferredLines,1,zoom,labels,vertical);
            result.Width=Math.Min(result.Width,availableWidth); result.Height=Math.Min(result.Height,availableHeight);
            return result;
        }

        static DockLayout Find(int count,int minimumLines,double size,double zoom,bool labels,bool vertical,double width,double height) {
            for(int lines=minimumLines;lines<=Math.Max(1,count);lines++) {
                DockLayout result=Measure(count,lines,size,zoom,labels,vertical);
                if(result.Width<=width+.000001 && result.Height<=height+.000001) return result;
            }
            return null;
        }

        static DockLayout Measure(int count,int lines,double size,double zoom,bool labels,bool vertical) {
            double labelHeight=labels?34:0;
            double tileWidth=labels?CaptionWidth(size):size+10;
            int along=Math.Max(1,(count+lines-1)/lines);
            var result=new DockLayout { IconSize=size, TileWidth=tileWidth, LabelHeight=labelHeight, Vertical=vertical, Lines=lines };
            if(vertical) {
                result.Rows=along; result.Columns=lines;
                result.CellWidth=Math.Max(size*zoom+14,tileWidth);
                // The caption follows the expanded image downwards. Reserve
                // hover plus click growth so it cannot meet the next row's icon.
                result.CellHeight=labels?size*Math.Min(1.85,zoom+.2)+labelHeight+14:size+10;
                result.GridWidth=tileWidth+(lines-1)*result.CellWidth;
                result.GridHeight=along*result.CellHeight;
                result.Width=result.GridWidth+size*(zoom-1)+56;
                result.Height=Math.Max(210,result.GridHeight+122);
            } else {
                result.Rows=lines; result.Columns=along;
                result.CellWidth=tileWidth;
                result.CellHeight=size*zoom+labelHeight+14;
                result.GridWidth=along*result.CellWidth;
                result.GridHeight=size+8+labelHeight+(lines-1)*result.CellHeight;
                result.Width=Math.Max(count==0?310:210,result.GridWidth+122);
                result.Height=size*zoom+66+labelHeight+(lines-1)*result.CellHeight;
            }
            return result;
        }

        internal static double CaptionWidth(double size) {
            double width=Math.Max(size+10,Math.Min(128,size*1.8+24));
            // Caption columns must shrink with the icon on unusually small
            // work areas instead of enforcing a wide fixed minimum.
            return size<24?Math.Max(size+2,width*size/24):width;
        }

        internal void Cell(int count,int index,out int row,out int column) {
            int start=0;
            for(int line=0;line<Lines;line++) {
                int length=count/Lines+(line<count%Lines?1:0);
                if(index<start+length) {
                    row=Vertical?index-start:line;
                    column=Vertical?line:index-start;
                    return;
                }
                start+=length;
            }
            row=column=0;
        }
    }
}
