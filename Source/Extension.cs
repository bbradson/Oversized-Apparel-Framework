// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the MIT license.
// If a copy of the license was not distributed with this file,
// You can obtain one at https://opensource.org/licenses/MIT/.

namespace OversizedApparel;
public class Extension : DefModExtension
{
	public Vector2 drawSize; // necessary because vanilla makes use of apparel <drawSize> for ground textures. Directly applying it without extension would result in many hats becoming slightly smaller.
}