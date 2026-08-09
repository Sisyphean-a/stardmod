---
处理方式: 裁决
状态: 关闭
认领者: "019fe46e-4c05-73b7-8bc6-5e9af0779752"
硬依赖: []
---

# 社区中心未解锁时的目的地状态

## 问题

社区中心的室外入口动作 `WarpCommunityCenter` 只有在 `ccDoorUnlock` 或 `JojaMember` 条件满足时才会传送。未解锁时，弹窗应如何呈现社区中心？

推荐：仍显示目的地但置灰，并标注“尚未开放”；不开始导航，也不把它当作不可达路线。

## 答案

社区中心始终出现在目的地列表中；当存档尚未满足 `ccDoorUnlock` 或 `JojaMember` 时，目的地置灰并标注“尚未开放”，点击不启动导航，也不报告为普通路线不可达。满足任一条件后恢复可选，按正常入口外安全停车规则导航。

## 依据

当前本地 Stardew Valley 1.6.15.24356 的 `Town` 地图在 `(52,19)`、`(53,19)` 使用 `WarpCommunityCenter`；原版 `GameLocation.performAction` 仅在 `ccDoorUnlock` 或 `JojaMember` 邮件条件满足时执行 `warpFarmer("CommunityCenter", 32, 23)`。用户确认采用置灰并标注“尚未开放”的方案。
