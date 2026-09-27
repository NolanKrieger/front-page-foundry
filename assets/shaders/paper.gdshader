// Aged newsprint: grain, soft blotches, horizontal fibres and a darker rim.
shader_type canvas_item;

uniform vec4 paper : source_color = vec4(0.937, 0.906, 0.839, 1.0);
uniform vec4 shade : source_color = vec4(0.835, 0.788, 0.690, 1.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
			mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
	vec2 px = FRAGCOORD.xy;
	float blotch = noise(px / 220.0) * 0.10 + noise(px / 55.0) * 0.05;
	float fibre = smoothstep(0.9, 1.0, noise(vec2(px.x / 700.0, px.y / 2.0))) * 0.06;
	vec2 uv = SCREEN_UV - 0.5;
	float rim = smoothstep(0.38, 0.9, length(uv * vec2(1.15, 1.0)));
	vec3 c = mix(paper.rgb, shade.rgb, clamp(blotch + fibre + rim * 0.6, 0.0, 1.0));
	c -= hash(floor(px)) * 0.035;
	COLOR = vec4(c, 1.0);
}
