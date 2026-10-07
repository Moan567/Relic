// ported from Skybox.fx's PixelShaderFunction
uniform samplerCube SkyBoxTexture;

varying vec3 TextureCoordinate;

void main()
{
    gl_FragColor = textureCube(SkyBoxTexture, normalize(TextureCoordinate));
}
