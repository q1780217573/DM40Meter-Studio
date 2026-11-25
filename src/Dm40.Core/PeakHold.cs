using System;

namespace Dm40.Core
{
    /// <summary>
    /// 峰值保持（Peak Hold，2026-11-25 新增）。
    /// 与 <see cref="MeasurementStatistics"/> 的区别：统计只给最小/最大/平均的数值，
    /// 峰值保持额外记住**极值出现的时刻**与单位，对应万用表上的 PEAK HOLD 语义，
    /// 便于事后定位"最大冲击/最低跌落发生在第几秒"。
    /// 线程安全由调用方保证（MeterSession 在帧回调线程里串行更新）。
    /// </summary>
    public sealed class PeakHold
    {
        /// <summary>最大工程值（SI 基本单位）。</summary>
        public double? Max { get; private set; }

        /// <summary>最大值出现的时刻。</summary>
        public DateTime? MaxAt { get; private set; }

        /// <summary>最小工程值（SI 基本单位）。</summary>
        public double? Min { get; private set; }

        /// <summary>最小值出现的时刻。</summary>
        public DateTime? MinAt { get; private set; }

        /// <summary>单位（随档位，取最近一次样本的显示单位）。</summary>
        public string Unit { get; private set; } = "";

        /// <summary>档位（VDC/TEMP/...）。</summary>
        public string Kind { get; private set; } = "";

        /// <summary>参与过峰值的样本数。</summary>
        public int Samples { get; private set; }

        public bool HasValue => Max.HasValue && Min.HasValue;

        /// <summary>喂一个样本。返回 true 表示极值发生了变化（供 UI 决定是否立即重绘）。</summary>
        public bool Update(double value, string unit, string kind, DateTime at)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;

            bool changed = false;
            if (!Max.HasValue || value > Max.Value)
            {
                Max = value;
                MaxAt = at;
                changed = true;
            }
            if (!Min.HasValue || value < Min.Value)
            {
                Min = value;
                MinAt = at;
                changed = true;
            }

            Unit = unit ?? "";
            Kind = kind ?? "";
            Samples++;
            return changed;
        }

        /// <summary>清零（切档或用户点「清零峰值」时调用）。</summary>
        public void Reset()
        {
            Max = null;
            MaxAt = null;
            Min = null;
            MinAt = null;
            Unit = "";
            Kind = "";
            Samples = 0;
        }
    }
}