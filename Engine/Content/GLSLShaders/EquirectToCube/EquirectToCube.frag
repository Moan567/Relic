// ported from EquirectToCube.fx's PSMain (technique EquirectToCube)
uniform vec3 FaceForward;
uniform vec3 FaceUp;
uniform vec3 FaceRight;

uniform sampler2D EquirectTexture;

const float PI = 3.14159265359;

varying vec2 ClipPosition;

void main()
{
    vec3 direction = normalize(FaceForward + ClipPosition.x * FaceRight + ClipPosition.y * FaceUp);

    vec2 uv;
    uv.x = 0.5 + atan(direction.x, -direction.z) / (2.0 * PI);
    uv.y = 0.5 - asin(direction.y) / PI;

    gl_FragColor = texture2D(EquirectTexture, uv);
}
