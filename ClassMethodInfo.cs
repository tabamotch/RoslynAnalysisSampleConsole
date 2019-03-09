using System.Collections.Generic;

namespace RoslynAnalysisSampleConsole
{
    public class ClassMethodInfo
    {
        public string ClassName { get; set; }

        public string MethodName { get; set; }

        public List<ClassMethodInfo> Children { get; }

        public int LineCount { get; set; }

        public ClassMethodInfo() : this(null, null, 0)
        {
            // オーバーロードコンストラクタの呼び出しのみ
        }

        public ClassMethodInfo(string className, string methodName, int lineCount = 0)
        {
            ClassName = className;
            MethodName = methodName;
            LineCount = lineCount;

            Children = new List<ClassMethodInfo>();
        }

        public static bool IsEquals(ClassMethodInfo c1, ClassMethodInfo c2) =>
            c1.ClassName == c2.ClassName && c1.MethodName == c2.MethodName;
    }
}