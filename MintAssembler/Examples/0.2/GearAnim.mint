module GObj.GearAnim;

namespace GObj
{
	extern object GearAnim
	{
		const GObj.GearAnim Obj();
		float frame()const;
	}
}

namespace HEL
{
	extern object Cast
	{
		float I2F(int);
	}
}

local object GearAnim
{
	void __Init() { }

	void WaitTillAnimFrame(int frames)
	{
		ref GObj.GearAnim animObj = GObj.GearAnim;
		animObj = animObj.Obj();
		animObj.waitTillAnimFrame(frames);
	}

	void waitTillAnimFrame(int frames)const
	{
		while (this.frame() < HEL.Cast.I2F(frames))
			yield 1;
	}
}