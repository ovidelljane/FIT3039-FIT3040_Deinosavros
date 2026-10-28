#ifndef MAP_CLOUD_NOISE_INCLUDED
#define MAP_CLOUD_NOISE_INCLUDED

float MapCloudHash(float2 p)
{
    p = frac(p * float2(123.34,345.45));
    p += dot(p,p+34.345);
    return frac(p.x*p.y);
}

float MapCloudNoise(float2 p)
{
    float2 cell = floor(p), f = frac(p);
    f = f*f*(3-2*f);
    return lerp(lerp(MapCloudHash(cell),MapCloudHash(cell+float2(1,0)),f.x),
        lerp(MapCloudHash(cell+float2(0,1)),MapCloudHash(cell+1),f.x),f.y);
}

float MapCloudShape(float2 p)
{
    return MapCloudNoise(p)*.72 + MapCloudNoise(p*2.03+7.17)*.28;
}

#endif
