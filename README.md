项目主要用来给Avalonia项目生成代码：

项目使用了`field`所以只支持`.NET 10.0`及以上版本，生成的代码需要配合`partial`使用。

1. 通过标记属性`[RaiseAndSetIfChanged]`自动生成支持`RaiseAndSetIfChanged(ReactiveUI.Avalonia)`的属性代码
    ```
    [RaiseAndSetIfChanged] public partial bool IsSelected { get; set; }
    ```
2. 通过标记属性`[ReactiveCommand]`自动生成支持`ReactiveCommand(ReactiveUI.Avalonia)`的命令代码
	```
    [ReactiveCommand]
    private void Close()
    {
        
    }
	```
	```
    [ReactiveCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        
    }
    private IObservable<bool> CanClose()
    {
        return Observable.Return(true);
    }
	```
3. 通过标记属性`[StyledProperty]`自动生成`StyledProperty(Avalonia)`的属性代码]
    ```
    [StyledProperty] public partial bool IsSelected { get; set; }
    ```