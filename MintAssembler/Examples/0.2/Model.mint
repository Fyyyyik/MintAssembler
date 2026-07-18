module GObj.Model;

namespace HEL.Math
{
	extern object Vector3
	{
		float x;
		float y;
		float z;
	}
}

namespace GObj
{
	extern object Model
	{
		void SetScale(float, float, float);
	}
}

local object Model
{
	void __Init() { }

	void SetScale(const ref HEL.Math.Vector3 scale)
	{
		GObj.Model.SetScale(scale.x, scale.y, scale.z);
	}
}