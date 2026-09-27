// The printing press: everything drawn on the world layers passes through here before the
// paper reaches the reader. Mid-tones become a 45° halftone screen, lines bleed a touch into
// the paper, and the one accent colour prints slightly out of register (GDD §11).
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec4 paper : source_color = vec4(0.937, 0.906, 0.839, 1.0);
uniform vec4 ink : source_color = vec4(0.118, 0.102, 0.086, 1.0);
uniform vec4 accent : source_color = vec4(0.66, 0.2, 0.16, 1.0);
uniform float halftone_cell = 2.2;
uniform float bleed = 0.35;
uniform float registration = 1.2;
/// How much of the halftoned print shows over the plain drawing (0 = off, 1 = full newsprint).
uniform float strength = 0.55;

float density(vec2 uv) {
	vec3 c = texture(screen_tex, uv).rgb;
	float lum = dot(c, vec3(0.299, 0.587, 0.114));
	float paper_lum = dot(paper.rgb, vec3(0.299, 0.587, 0.114));
	return clamp((paper_lum - lum) / (paper_lum - 0.08), 0.0, 1.0);
}

float accentness(vec2 uv) {
	vec3 c = texture(screen_tex, uv).rgb;
	return clamp((c.r - max(c.g, c.b) - 0.12) * 4.0, 0.0, 1.0);
}

void fragment() {
	vec2 px = 1.0 / vec2(textureSize(screen_tex, 0));
	vec4 src = texture(screen_tex, SCREEN_UV);

	// Ink bleed: a soft one-pixel spread, then a threshold that fattens strong lines.
	float d = density(SCREEN_UV);
	float spread = (density(SCREEN_UV + vec2(px.x, 0.0)) + density(SCREEN_UV - vec2(px.x, 0.0))
		+ density(SCREEN_UV + vec2(0.0, px.y)) + density(SCREEN_UV - vec2(0.0, px.y))) * 0.25;
	float bled = max(d, spread * bleed);

	// Halftone: mid-tones become dots on a 45° screen; faint tone, solid ink and blank paper pass through.
	vec2 r = mat2(vec2(0.7071, -0.7071), vec2(0.7071, 0.7071)) * FRAGCOORD.xy / halftone_cell;
	float dot_dist = length(fract(r) - 0.5) * 1.4142;
	float tone = smoothstep(0.12, 0.9, bled);
	float dots = step(dot_dist, tone);
	float mask = mix(dots, 1.0, step(0.9, bled)) * step(0.12, bled);

	// The accent channel prints a hair off register.
	float a = accentness(SCREEN_UV + vec2(registration, registration * 0.5) * px);

	vec3 halftoned = mix(src.rgb, ink.rgb, mask);
	vec3 printed = mix(src.rgb, halftoned, strength);
	// Keep true solid ink crisp: full-density source pixels stay exactly ink.
	printed = mix(printed, ink.rgb, step(0.98, d));
	printed = mix(printed, accent.rgb, a);
	COLOR = vec4(printed, 1.0);
}
