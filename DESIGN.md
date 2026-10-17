---
name: Codex 工具箱
description: 以 Apple/macOS System Settings 为审美依据的 Windows 原生设置工具
colors:
  ink: "#202124"
  muted: "#62656B"
  accent: "#2866DB"
  accent-hover: "#005FC8"
  accent-pressed: "#0056B5"
  ground: "#F1F5FA"
  sidebar: "#E8EEF8"
  group: "#FFFFFF"
  button: "#F5F7FC"
  button-hover: "#E9EFFA"
  button-pressed: "#DFE8F8"
  field-border: "#CCD7E7"
  text-selection: "#B7D7FF"
  divider: "#ECECF0"
  success: "#247043"
  failure: "#AC382F"
  warning: "#805812"
  table-header: "#EAF0F9"
  table-alternate: "#F5F8FD"
typography:
  headline:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "27 DIP"
    fontWeight: 600
    lineHeight: "41 DIP"
  dialog-title:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "24 DIP"
    fontWeight: 600
    lineHeight: "36 DIP"
  title:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "17 DIP"
    fontWeight: 600
    lineHeight: "26 DIP"
  body:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "14 DIP"
    fontWeight: 400
    lineHeight: "21 DIP"
  label:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "13 DIP"
    fontWeight: 400
    lineHeight: "20 DIP"
  hint:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "12 DIP"
    fontWeight: 400
    lineHeight: "18 DIP"
  ip:
    fontFamily: "Segoe UI"
    fontSize: "29 DIP"
    fontWeight: 600
    lineHeight: "38 DIP"
rounded:
  group: "18 DIP"
  button: "11 DIP"
  field: "9 DIP"
spacing:
  related: "8 DIP"
  label-gap: "6 DIP"
  field-before: "15 DIP"
  group-after: "20 DIP"
  row-block: "19 DIP"
  group-padding: "24 DIP"
  list-padding: "22 DIP"
  profile-padding: "23 DIP"
  header-after: "24 DIP"
  page-start: "32 DIP"
components:
  button:
    backgroundColor: "{colors.button}"
    textColor: "{colors.ink}"
    rounded: "{rounded.button}"
    padding: "8 DIP 15 DIP"
  button-hover:
    backgroundColor: "{colors.button-hover}"
  button-pressed:
    backgroundColor: "{colors.button-pressed}"
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.group}"
    rounded: "{rounded.button}"
    padding: "8 DIP 15 DIP"
  button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
  button-primary-pressed:
    backgroundColor: "{colors.accent-pressed}"
  field:
    backgroundColor: "{colors.group}"
    textColor: "{colors.ink}"
    rounded: "{rounded.field}"
    padding: "7 DIP 10 DIP"
  group:
    backgroundColor: "{colors.group}"
    rounded: "{rounded.group}"
    padding: "24 DIP"
  navigation-selected:
    backgroundColor: "{colors.group}"
    textColor: "{colors.ink}"
    rounded: "{rounded.button}"
    padding: "11 DIP 8 DIP 11 DIP 12 DIP"
  profile-ip:
    textColor: "{colors.ink}"
    typography: "{typography.ip}"
---

# Design System: Codex 工具箱

## Overview

**Creative North Star: "Apple/macOS System Settings"**

安静、清楚、有秩序的设置界面。浅灰侧栏承担位置感，近白底面承托白色分组，蓝色提示可执行操作与键盘焦点。此方向由用户明确指定，不另造品牌隐喻，也不把熟悉的原生工具改成营销页面。

实现使用 Windows WPF，保留 Windows 标题栏、窗口按钮、文件选择器和键盘约定。中文使用系统字体，实测 IP 通过更大的数字字体成为阅读重点；操作反馈紧邻当前任务，未知状态以文字说明。

**Key Characteristics:**
- 浅灰侧栏、白色内嵌选中项、分组设置内容。
- 克制蓝色操作、独立文字状态、无装饰性阴影。
- 原生 DIP 尺寸与清晰中文，随 Windows DPI 缩放。
- 单次短淡入，遵守 Windows 客户区动画设置。

本文件从 `Theme.xaml`、`UI.cs`、`Dialogs.cs` 提取当前实现；`.impeccable/surface.md` 保存此轮页面方向和验收边界。这里只记录已有系统，未声明深色主题或手机布局。

## Colors

配色以接近纸面的中性灰为主，操作蓝与成功、失败文字提供必要信号。精确色值以上方 token 为准。

### Primary

- **操作蓝**（`accent`）：主要按钮、正在检查的文字、键盘焦点环。
- **深一阶操作蓝**（`accent-hover` / `accent-pressed`）：主按钮悬停与按下背景；文字仍为白色。

### Neutral

- **正文墨色**（`ink`）：标题、正文、普通按钮文字、IP。
- **辅助灰**（`muted`）：说明、标签、侧栏图标、时间和准备状态。
- **近白底面**（`ground`）、**侧栏灰**（`sidebar`）、**分组白**（`group`）：建立界面层次；选中导航也用分组白。
- **按钮灰**（`button` / `button-hover` / `button-pressed`）：普通操作的三个背景状态。
- **字段边界**（`field-border`）、**浅蓝文字选区**（`text-selection`）、**分隔灰**（`divider`）：编辑和阅读辅助。
- **表头灰**（`table-header`）、**交替行灰**（`table-alternate`）：结果表格的轻量层次。

成功与失败分别使用 `success`、`failure`，并配具体状态文案；删除线路仅将普通按钮文字改为失败色。没有第二套装饰性强调色。

**The Status Text Rule.** 结果必须保留文字含义；蓝色检查中、绿色符合预期、红色失败不能代替可读结论。

## Typography

**Body Font:** Microsoft YaHei UI。窗口明确设置该字体，子控件继承。

**Numeric IP Font:** Segoe UI。仅实测 IP 使用独立字体与较大行高；不是等宽字体。

层级来自字号和 SemiBold，不依赖大写、字距装饰或自定义字体。WPF `TextBlock` 默认开启换行。UI.Text 显式使用 ceil(字号 × 1.5) 行高，避免大标题继承较小正文行高；手动线路数字保留 38 DIP 行高，自动线路数字为 20 DIP 字号、30 DIP 行高。按钮不使用 TextBlock 行高作为高度依据。

### Hierarchy

- **Headline**：主页面标题，见 `headline`。
- **Dialog title**：线路编辑和结果窗口标题，见 `dialog-title`。
- **Title**：设置组及线路名称，见 `title`；工具行和帮助小标题另有 16 DIP SemiBold。
- **Body / Label / Hint**：正文、字段标签与说明，见对应 token。侧栏名称为 14 DIP；品牌为 20 DIP SemiBold，底部次要说明为 11 DIP。
- **IP**：见 `ip`；点击有效实测 IP 可复制，有原生提示文字。

## Layout

所有本机尺寸单位为 **DIP（1/96 英寸）**。144 DPI 的截图对应 150% 缩放，1 DIP 对应 1.5 个设备像素；不要把截图像素值回写为 WPF 尺寸。侧车里的 HTML/CSS 是供设计面板展示的近似转换，CSS px 对应未缩放 DIP，不是运行时实现。

主窗口声明外尺寸 1040 × 752 DIP，最小外尺寸 880 × 610 DIP。真实 144 DPI 客户区截图分别约为 1025.33 × 712 和 865.33 × 570 DIP；差值来自原生窗口边框与标题栏。

侧栏固定 218 DIP，右侧填充余量。主内容外边距按 WPF 左、上、右、下顺序为 32、25、30、18 DIP；头部下留 24 DIP，标题与说明之间留 8 DIP。页面采用固定头部、纵向滚动正文、底部反馈三行，水平滚动关闭。校园网启用/更新操作位于固定头部。

白色组默认内边距 20 DIP、组后距 18 DIP。工具列表采用左右 22、上下 3 DIP 内边距；线路列表采用左右 23、上下 0 DIP，行上下各 19 DIP，并以 1 DIP 分隔线连接。行内左侧信息占剩余宽度，右侧按钮按内容宽度排布。

线路对话框声明 580 × 690 DIP，最小 510 × 480；外边距 28、22、28、22 DIP。标题与底部操作固定，中间表单纵向滚动。结果窗口为 830 × 535 DIP，最小 680 × 410，外边距 25 DIP；表格填充中间空间。额度工具窗口为 340 × 245 DIP，最小 300 × 220，内边距 19 DIP。

当前实现没有响应式断点或侧栏折叠机制；缩小时通过正文和表单滚动保持内容可达。

## Elevation & Depth

界面不使用应用自绘阴影、渐变、玻璃或模糊。近白底、侧栏灰、白色组和轻分隔线形成层次，焦点环只表达键盘位置。Windows 自身管理顶层窗口的边框和阴影，不把系统窗口效果当作组容器的设计 token。

## Shapes

分组、按钮、字段的圆角分别见 `rounded.group`、`rounded.button`、`rounded.field`。组没有描边；字段使用 1 DIP 边界；列表用 1 DIP 水平细线。按钮主题没有可见边框。分组 Border 设置圆角但未设置 `ClipToBounds`，因此不保证任意子内容都按圆角裁切。

焦点装饰单独叠在控件之上：按钮为 2 DIP 蓝边、8 DIP 圆角、Margin -3 DIP；字段为 2 DIP 蓝边、7 DIP 圆角、Margin -1 DIP。显示焦点不改变控件测量尺寸。

## Components

### Buttons

安静的实色操作，默认最小高度 36 DIP。内容左右居中，使用 `button` 或 `button-primary` token；辅助构建函数默认按钮右侧留 8 DIP。

悬停和按下改变背景色，主按钮分别采用 `accent-hover`、`accent-pressed`，普通按钮采用对应灰色。主题没有为 hover 设置整控件透明度，也没有悬停位移。禁用时只将按钮 Body 的 Opacity 设为 0.45。键盘焦点使用 Shapes 中的独立叠层。

### Inputs / Fields

白底、细边、最小高度 38 DIP。字段标签放在输入框上方，前距 15 DIP、后距 6 DIP；多行框高 90 DIP，顶部对齐并可纵向滚动。选区使用 `text-selection`，只读状态移除字段描边。

设置页的密钥输入使用原生 PasswordBox，单独设置 10、8 DIP 内边距和 38 DIP 最小高度；不能假定它拥有自定义 TextBox 模板。CheckBox、Expander、滚动条和文件/文件夹选择器保留 Windows 原生外观。

### Cards / Containers

白色分组组织同类设置，依靠留白和分隔线形成秩序；没有卡片悬浮、阴影或独立彩色标题条。内边距与行距遵循 Layout 中的实际用途。

### Navigation

未选中背景透明，选中为白色；悬停和按下继承普通按钮背景状态。侧栏导航外侧左右各 12 DIP，项内边距 12、11、8、11 DIP，项外距上 2、下 3 DIP。19 DIP 线条图标以 1.65 DIP 笔画绘制，右侧与文字间距 11 DIP。

代码为选中按钮设置 SemiBold，但其子 TextBlock 显式设置 Normal，因此当前可见导航标签保持普通字重；不要宣称选中标签已有粗体样式。

页面切换对正文执行一次 Opacity .86 → 1 的 130 ms 淡入，使用 WPF `ExponentialEase`、`EaseOut`、Exponent 4。仅在 `SystemParameters.ClientAreaAnimation` 为真时执行；没有位移动画。

### Profile Results

线路名称、代理、实测 IP、文字状态和详情纵向排列。检查开始时立即更新检查中文案、使用操作蓝并禁用重复检查；原有 IP 保留到新结果到达。成功或失败通过文字状态及语义色更新。此模式不把代理出口观测等同于应用连接证明。

### Result Table / Owned Windows

原生只读 DataGrid：无网格线和行头、表头可见、支持扩展选择及带表头复制。表头内边距 14、12 DIP、SemiBold；单元格内边距 14、11 DIP；文字换行。三列为 155 DIP、剩余宽度、110 DIP。白色组包裹表格，组内边距为零。

结果窗口在顶部显示准备、检查中、完成或具体错误；底部保留复制、重新检查、关闭。检查中禁用重复检查与复制，完成后按实际结果恢复。线路窗口 Enter 保存、Esc 取消；结果窗口 Esc 关闭。主界面支持 F5 / Ctrl+R、Ctrl+N、Ctrl+1–5 与 F1。

## Do's and Don'ts

### Do:

- **Do** 延续用户确认的 Apple/macOS System Settings 审美，同时保留 Windows 原生窗口与键盘习惯。
- **Do** 使用 DIP 记录原生尺寸，并区分窗口外尺寸、客户区和截图设备像素。
- **Do** 复用已有白色分组、灰色辅助文字、蓝色操作和显式状态文案。
- **Do** 保留独立焦点叠层与系统动画偏好检查。

### Don't:

- **Don't** 添加仿 macOS 红黄绿窗口按钮、玻璃装饰、位图装饰或私有字体。
- **Don't** 把悬停改为整控件透明度变化，或让焦点环引起布局移动。
- **Don't** 用颜色取代状态文字，或把未知观测写成确定成功。
- **Don't** 将面板用 HTML/CSS 预览、合成色阶或未实现的响应式规则写成 WPF 现状。

## 2.2 渐进展开
首页保留自动检查、结论与出口采样，手动线路折叠。额度单独一页，默认只显示已发现窗口、登录状态与添加动作，位置详情按需展开。校园页保留保护状态、当前网络与启用动作，自定义规则折叠；设置页把连接参数、诊断工具、备份迁移分为三项折叠入口。最小 880 × 610 DIP 时纵向滚动，操作标签保持完整。

## 2.3 视觉参考与自适应
用户提供 Cockpit 截图作为参考。侧栏为 #E8EEF8，选中项 #D6E3FC、文字与图标 #245DD7；主底面 #F1F5FA。分组背景 #FCFDFF，1 DIP #DDE4EF 描边，18 DIP 圆角，24 DIP 内边距，20 DIP 后距。侧栏未选中项无边框。普通按钮 11 DIP 圆角、42 DIP 最小高；细滚动条与折叠箭头使用同套蓝灰色。

主窗口声明 1120×800 DIP，最小 600×460。逻辑宽度不足 900 时改为 76 DIP 导航栏；正常侧栏 218 DIP。内容区最大 1360 DIP。卡片按可用逻辑宽度 740、1140 两个断点选择最多一、二、三列，并受实际项目数量限制。应用缩放 75%–150% 通过 WPF LayoutTransform 重新度量，不放大截图；系统每显示器 DPI 保持 PerMonitorV2。短窗口减少说明占位，弹窗保留滚动与当前显示器工作区边界。既有字号/分组段落记录 2.2 的基线，以上 2.3 值为当前组件实现。
