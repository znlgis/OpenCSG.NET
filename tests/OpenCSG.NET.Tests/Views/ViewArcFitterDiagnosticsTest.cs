using System.Threading;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class ViewArcFitterDiagnosticsTest
    {
        [Test]
        [Explicit("DEFECT-001: ViewArcFitter.DiagnosticsEnabled/LastDiagnostics 非线程隔离，并行测试下串扰")]
        public void DiagnosticsEnabled_IsNotVisibleOnOtherThreads()
        {
            var segments = new[]
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(1, 0), true),
                new ViewSegment(new ViewPoint2(1, 0), new ViewPoint2(2, 0), true),
            };

            ViewArcFitter.DiagnosticsEnabled = true;
            ViewArcFitter.Fit(segments);   // 触发一次写入 LastDiagnostics

            bool? seenOnOtherThread = null;
            var thread = new Thread(() => seenOnOtherThread = ViewArcFitter.DiagnosticsEnabled);
            thread.Start();
            thread.Join();

            try
            {
                // 期望：诊断开关是线程局部状态，另一个线程不应看到它。
                // 实际（DEFECT-001）：普通 static ⇒ 另一线程读到 true，本断言失败。
                Assert.That(seenOnOtherThread, Is.False,
                    "DEFECT-001：另一线程读到了 DiagnosticsEnabled=true，说明它是进程级全局态");
            }
            finally
            {
                ViewArcFitter.DiagnosticsEnabled = false;
            }
        }
    }
}
