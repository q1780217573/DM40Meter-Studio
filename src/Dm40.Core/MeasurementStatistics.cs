namespace Dm40.Core
{
    /// <summary>
    /// 测量统计（PRD FR-006、§5.4）。
    /// 统计对象为当前窗口内全部有效主读数工程值（<see cref="Dm40.Protocol.Measurement.NormValue"/>）。
    /// 无效帧（过载 / NormValue 为 null）不计入；"清零重计"后自下一帧重新累计。
    /// 本类不依赖时间——时长由调用方（会话）基于样本时间戳计算，便于注入时钟测试。
    /// 线程安全由调用方保证（核心层在单线程事件聚合器上调用）。
    /// </summary>
    public sealed class MeasurementStatistics
    {
        private double _min;
        private double _max;
        private double _sum;
        private int _count;

        /// <summary>有效样本数。</summary>
        public int Count => _count;

        /// <summary>最小值；无样本为 null。</summary>
        public double? Min => _count == 0 ? (double?)null : _min;

        /// <summary>最大值；无样本为 null。</summary>
        public double? Max => _count == 0 ? (double?)null : _max;

        /// <summary>平均值 Σ/N；无样本为 null。</summary>
        public double? Avg => _count == 0 ? (double?)null : _sum / _count;

        /// <summary>峰峰值 Δ = Max − Min；无样本为 null。</summary>
        public double? Delta => _count == 0 ? (double?)null : _max - _min;

        /// <summary>
        /// 累计一个有效样本。仅传入有效工程值（调用方负责过滤过载/无效帧）。
        /// </summary>
        public void Add(double value)
        {
            if (_count == 0)
            {
                _min = _max = value;
                _sum = value;
                _count = 1;
                return;
            }
            if (value < _min) _min = value;
            if (value > _max) _max = value;
            _sum += value;
            _count++;
        }

        /// <summary>清零重计：自下一帧起重新累计。</summary>
        public void Reset()
        {
            _min = _max = _sum = 0;
            _count = 0;
        }
    }
}
