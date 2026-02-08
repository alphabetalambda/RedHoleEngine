namespace RedHoleEngine.Physics;

public class StringSystem
{
    struct Point
    {
        public int x;
        public int y;
        public int r;
    }

    public class String
    {
        private Point Point1;
        private Point Point2;
        private float length;
        public void ToString(string text)
        {
            Point1 = new Point();
            Point2 = new Point();
        }
    } 
}