using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dm40.App.ViewModels
{
    /// <summary>ViewModel 基类：实现 INotifyPropertyChanged。</summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>赋值并在变化时触发通知。返回是否发生变化。</summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
