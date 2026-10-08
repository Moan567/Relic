using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU;
public static class FenceWaiter
{
    public static void Wait(GL gl, nint fence, string label, float progress01)
    {
        uint flags = (uint)GLEnum.SyncFlushCommandsBit;

        while (true)
        {
            var status = (GLEnum)gl.ClientWaitSync(fence, flags, 50000000);
            flags = 0;

            if (status == GLEnum.AlreadySignaled || status == GLEnum.ConditionSatisfied)
            {
                return;
            }

            if (status == GLEnum.WaitFailed)
            {
                throw new InvalidOperationException("glClientWaitSync failed.");
            }

            SpinnerConsole.Report(label, progress01);
            Thread.Yield();
        }
    }
}