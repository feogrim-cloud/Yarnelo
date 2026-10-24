using System;
using System.Collections.Generic;

namespace Polka {
    internal static class DockLayoutTests {
        static int assertions;
        static void Check(bool value,string message) { if(!value) throw new Exception(message); assertions++; }
        static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        static void Cells(DockLayout layout,int count) {
            var occupied=new HashSet<string>();
            int previousRow=-1,previousColumn=-1;
            for(int index=0;index<count;index++) {
                int row,column; layout.Cell(count,index,out row,out column);
                Check(row>=0 && row<layout.Rows && column>=0 && column<layout.Columns,"each shortcut has an available cell");
                Check(occupied.Add(row+":"+column),"no two shortcuts occupy the same cell");
                if(index>0) Check(layout.Vertical ? column>previousColumn || (column==previousColumn && row>previousRow) :
                    row>previousRow || (row==previousRow && column>previousColumn),"reading order follows the dock orientation");
                previousRow=row; previousColumn=column;
            }
        }
        public static int Main() {
            try {
                var wide=DockLayout.Create(30,1,80,1.65,true,false,1900,1040);
                Check(wide.IconSize==80 && wide.Rows==3,"30 maximum-size icons reserve readable caption width by wrapping to three rows");
                var tall=DockLayout.Create(30,1,80,1.65,true,true,1900,1040);
                Check(tall.IconSize==80 && tall.Columns==8,"a vertical dock reserves space for hovered two-line names by adding columns");
                var narrow=DockLayout.Create(30,1,80,1.65,true,false,500,4000);
                Check(narrow.IconSize==80 && narrow.Rows>4,"a narrow portrait work area may use more than four effective rows");
                var shortArea=DockLayout.Create(30,1,80,1.65,true,true,6000,500);
                Check(shortArea.IconSize==80 && shortArea.Columns>4,"a short work area may use more than four effective side columns");
                var small=DockLayout.Create(30,1,80,1.65,true,false,640,400);
                Check(small.IconSize>12 && small.IconSize<80 && small.Width<=640.001 && small.Height<=400.001,
                    "an unusually small work area reduces size only after full-size arrangements cannot fit");
                foreach(bool vertical in new[]{false,true}) foreach(bool labels in new[]{false,true})
                foreach(double size in new[]{24.0,52.0,80.0}) foreach(double zoom in new[]{1.0,1.28,1.65})
                foreach(var area in new[]{new[]{640.0,400.0},new[]{1024.0,688.0},new[]{1280.0,680.0},new[]{1900.0,1040.0},new[]{3440.0,1400.0}})
                for(int count=0;count<=30;count++) for(int preferred=1;preferred<=4;preferred++) {
                    DockLayout layout=DockLayout.Create(count,preferred,size,zoom,labels,vertical,area[0],area[1]);
                    Check(Finite(layout.Width) && Finite(layout.Height) && Finite(layout.IconSize),"layout measurements are finite");
                    Check(layout.Width<=area[0]+.001 && layout.Height<=area[1]+.001,"dock fits both work area dimensions");
                    Check(layout.IconSize>0 && layout.IconSize<=size+.001,"effective size remains positive and never exceeds preference");
                    Check(layout.Lines>=Math.Min(preferred,Math.Max(1,count)),"user-selected rows or columns remain the preferred minimum");
                    Check(layout.GridWidth<=layout.Width && layout.GridHeight<=layout.Height,"icon grid leaves room for its controls");
                    Cells(layout,count);
                }
                Console.WriteLine("PASS: "+assertions+" dock layout assertions; no desktop windows were opened.");
                return 0;
            } catch(Exception ex) { Console.WriteLine(ex); return 1; }
        }
    }
}
