using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pdfReader
{
    public class Word
    {
        private String Text;
        private Rectangle BoundingBox;
        public Word(String text, Rectangle rectangle)
        {
            Text = text;
            BoundingBox = rectangle;
        }
        public String getText() { return Text; }
        public void setText(String text) { Text = text; }
        public Rectangle getBoundingBox() { return BoundingBox; }
        public void setBoundingBox(Rectangle rectangle) { BoundingBox = rectangle; }
        public String toString() { return Text + " " + BoundingBox.toString(); }
    }

    public class Rectangle
    {
        private int x1;
        private int y1;
        private int x2;
        private int y2;
        public int getX1()
        { return x1; }

        public int getY1() { return y1; }
        public int getX2() { return x2; }
        public int getY2() { return y2; }
        public void setX1(int x) { x1 = x; }
        public void setY1(int y) { y1 = y; }
        public void setX2(int x) { x2 = x; }
        public void setY2(int y) { y2 = y; }
        public Rectangle(int x1, int y1, int x2, int y2)
        {
            this.x1 = x1;
            this.y1 = y1;
            this.x2 = x2;
            this.y2 = y2;
        }
        public String toString()
        {
            return $"{x1},{y1} -> {x2},{y2}";
        }
    }

}
