using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Protocol.Tests
{
    /// <summary>
    /// 档位/量程目录单元测试：保证 ModeCatalog 与 FlagInfo 一一对应、Key 可分发、命令帧字节正确。
    /// </summary>
    [TestClass]
    public class ModeCatalogTests
    {
        [TestMethod]
        public void All_CoversEveryFlagInfoEntry()
        {
            Assert.AreEqual(ProtocolConstants.FlagInfo.Count, ModeCatalog.All.Count);
            CollectionAssert.AreEquivalent(
                ProtocolConstants.FlagInfo.Keys.ToList(),
                ModeCatalog.All.Select(e => e.Flag).ToList());
        }

        [TestMethod]
        public void Keys_AreUnique_AndFindable()
        {
            var keys = ModeCatalog.All.Select(e => e.Key).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "存在重复 Key");
            foreach (var e in ModeCatalog.All)
                Assert.AreSame(e, ModeCatalog.Find(e.Key), "Find 未命中 " + e.Key);
        }

        [TestMethod]
        public void KeyShape_IsAsciiOnly()
        {
            foreach (var e in ModeCatalog.All)
                foreach (var ch in e.Key)
                    Assert.IsTrue(ch < 128, "Key 含非 ASCII 字符: " + e.Key);
        }

        [TestMethod]
        public void BuildCommand_EncodesFlagAtByte5()
        {
            foreach (var e in ModeCatalog.All)
            {
                var f = ModeCatalog.BuildCommand(e.Flag);
                Assert.AreEqual(6, f.Length);
                Assert.AreEqual(0xAF, f[0]);
                Assert.AreEqual(0x05, f[1]);
                Assert.AreEqual(0x03, f[2]);
                Assert.AreEqual(0x06, f[3]);
                Assert.AreEqual(0x01, f[4]);
                Assert.AreEqual(e.Flag, f[5]);
            }
        }

        [TestMethod]
        public void BuildCommand_WithChecksum_SumsToZero()
        {
            // 传输层会追加第 7 字节 (-sum)&0xFF；拼上后整帧和 &0xFF 必须为 0
            var f = ModeCatalog.BuildCommand(0x28); // VDC AUTO
            int sum = f.Sum(b => b);
            byte chk = (byte)((-sum) & 0xFF);
            Assert.AreEqual(0, (sum + chk) & 0xFF);
            CollectionAssert.AreEqual(new byte[] { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x28, chk }, f.Concat(new[] { chk }).ToArray());
        }

        [TestMethod]
        public void Order_VdcGroupStartsAt600mV()
        {
            var vdc = ModeCatalog.ForKind("VDC");
            Assert.IsTrue(vdc.Count >= 7, "VDC 档位数量异常: " + vdc.Count);
            Assert.AreEqual("600mV", vdc.First().Range);
            Assert.AreEqual("AUTO", vdc[vdc.Count - 2].Range);
            Assert.AreEqual("AUTO+", vdc.Last().Range);
        }

        [TestMethod]
        public void Ranges_WithinEachKind_AreSortedAscendingThenAuto()
        {
            foreach (var kind in ModeCatalog.Kinds)
            {
                var group = ModeCatalog.ForKind(kind);
                if (group.Count <= 1) continue;
                int firstAuto = group.ToList().FindIndex(e => e.IsAuto);
                if (firstAuto <= 0) continue;
                // AUTO 之前必须是固定量程，AUTO 之后只能还是 AUTO 系
                for (int i = firstAuto; i < group.Count; i++)
                    Assert.IsTrue(group[i].IsAuto, $"{kind} 的 AUTO 之后出现固定量程: {group[i].Range}");
            }
        }

        [TestMethod]
        public void KnownKeys_Exist()
        {
            foreach (var key in new[]
                     {
                         "vdc-600mv", "vdc-auto", "vdc-auto-plus",
                         "vac-auto", "adc-auto", "aac-auto",
                         "res-600ohm", "res-online-auto",
                         "cap-auto", "diode-auto", "cont-auto", "freq-auto", "temp-auto",
                     })
                Assert.IsNotNull(ModeCatalog.Find(key), "缺少 Key: " + key);
        }

        [TestMethod]
        public void CurrentUiModes_AreCovered()
        {
            // 2026-10-26 版 UI 的 10 个档位按钮必须仍能在目录里找到（AUTO 量程）
            foreach (var key in new[] { "vdc-auto", "vac-auto", "adc-auto", "aac-auto", "res-auto",
                                        "cap-auto", "diode-auto", "cont-auto", "freq-auto", "temp-auto" })
                Assert.IsNotNull(ModeCatalog.Find(key), "UI 档位缺少对应 Key: " + key);
        }
    }
}