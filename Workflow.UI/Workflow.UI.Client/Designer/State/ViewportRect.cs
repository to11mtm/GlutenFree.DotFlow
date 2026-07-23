// <copyright file="ViewportRect.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

/// <summary>
/// 📐 The canvas viewport's measured client rect (from <c>dotflowCanvas.measure</c>): size plus
/// client-space origin. The origin lets pointer math use target-independent
/// <c>Client − rect</c> coordinates (G1). Framework-free~ ✨.
/// </summary>
/// <param name="Width">The viewport width in CSS pixels.</param>
/// <param name="Height">The viewport height in CSS pixels.</param>
/// <param name="Left">The viewport's client-space left edge.</param>
/// <param name="Top">The viewport's client-space top edge.</param>
public readonly record struct ViewportRect(double Width, double Height, double Left, double Top);
