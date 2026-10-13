# PcInsight

一款 Windows 硬件 / 软件信息查看工具。单文件、免安装、双击即用，无需管理员权限，开箱呈现这台电脑的完整画像。

基于 WinUI 3 + .NET 9 构建，原生 Windows 11 风格界面，支持深色 / 浅色主题与中英文双语。

## 功能

信息在后台并行采集，页面随采随显，导航栏徽标实时显示每项的条目数。

**硬件信息**

- 总览：计算机、操作系统、主板固件、运行环境一页速览
- 处理器：型号、核心 / 线程、频率、缓存、负载
- 内存：容量、类型、频率、插槽占用
- 存储：物理磁盘、分区与可用空间、S.M.A.R.T. 健康度
- 显示与显卡：GPU、驱动、当前分辨率与显示器
- 网络：网卡、IPv4 / IPv6、MAC、网关
- 音频：声卡设备与状态
- 电池与电源：电量、化学类型、设计容量、AC 状态
- USB 与外设：USB 控制器与已连接设备
- 打印机、安全（TPM / 安全启动 / Defender / UAC）、传感器与散热（温度 / 风扇）

**软件信息**

- 已安装软件：Win32 与 UWP/MSIX 应用，含版本、发行者、安装日期
- 更新记录（热补丁）、服务、启动项、驱动程序
- 正在运行的进程：PID、内存占用、线程数、发行者
- 系统环境：环境变量（用户 / 系统级）

**其他特性**

- 一键重新扫描
- 关键字搜索 / 过滤
- 导出为 JSON、纯文本，或复制到剪贴板
- 中英文界面实时切换
- 全部数据在本地通过 WMI（CIM）与系统 API 采集，不联网、不上报

## 系统要求

- **运行**：Windows 10 版本 2004（内部版本 19041）及以上，x64 或 ARM64
- **构建**：Windows + [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- 自包含发布，目标机器无需安装 .NET 运行时

## 获取与使用

### 直接使用发布版

在本仓库的 Releases 页面下载对应架构的 `PcInsight.exe`，双击运行。单个可执行文件约 34 MB，不写注册表、不安装服务；删除文件即完成卸载。

### 从源码构建

```powershell
# x64 单文件绿色版
dotnet publish PcInsight\PcInsight.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# ARM64 单文件绿色版
dotnet publish PcInsight\PcInsight.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```

产物位于 `PcInsight\bin\Release\net9.0-windows10.0.26100.0\<RID>\publish\PcInsight.exe`。

开发调试：

```powershell
dotnet run --project PcInsight
```

### 自检命令行参数

| 参数 | 作用 |
| --- | --- |
| `--selftest` | 无头模式跑全部数据采集器，输出有效值数量与 JSON / 文本转储，用于验证数据层 |
| `--uicheck` | 无头模式启动 UI，等待采集完成后遍历可视化树并输出报告 |
| `--uicheck=<秒>` | 同上，自定义等待时长（如 `--uicheck=3` 可验证渐进加载中的占位态） |

## 技术栈

- [WinUI 3](https://learn.microsoft.com/windows/apps/winui/) / Windows App SDK 2.5.1，解包（unpackaged）+ 自包含部署
- .NET 9，C# 13，XAML 声明式界面
- WMI / CIM 查询经 [Microsoft.Management.Infrastructure](https://www.nuget.org/packages/Microsoft.Management.Infrastructure)（MMI）
- Release 启用 ILLink 裁剪（partial trim）、单文件压缩，并剔除应用不使用的 Windows AI / WebView2 / CLR 诊断组件等传递负载

## 项目结构

```
PcInsight/
├─ PcInsight.csproj        # 发布、裁剪与 RegFree WinRT 清单配置
├─ app.manifest            # DPI 感知、长路径、UTF-8 代码页
├─ App.xaml(.cs)           # 应用入口、selftest / uicheck 模式
├─ MainWindow.xaml(.cs)    # 主窗口、自定义标题栏、导航、导出
├─ Views/                  # SectionPage 与 XAML 转换器 / 本地化绑定
└─ Core/                   # 与界面无关的数据层（共享项目）
   ├─ PcInfo.cs            # 19 个采集任务的调度与渐进式回报
   ├─ Hardware.cs/Hardware2.cs  # 硬件采集器
   ├─ Software.cs          # 软件采集器
   ├─ Wmi.cs               # CIM 查询封装
   ├─ SysInfo.cs / Native.cs   # 系统 API 与 P/Invoke
   ├─ Model.cs             # Snapshot / Section / Group 数据模型
   └─ Localization.cs      # 中英文资源
```

## 隐私

程序只读取本机系统信息，不包含任何网络上报或遥测代码。导出的 JSON / 文本仅在用户主动操作时写入本地。
