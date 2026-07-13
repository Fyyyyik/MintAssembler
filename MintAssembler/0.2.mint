module User.Tsuruoka.MintTest;

namespace HEL.Math
{
	mint object Vector3
	{
		float x;
		void SetX(float);
		float GetX();
	}
}

namespace Scn.Step.Hero
{
	mint object ButtonMask
	{
		int L();
		int R();
		int D();
		int U();
	}
}

namespace GObj
{
	mint object FootState
	{
		bool IsGround();
	}

	mint object Target
	{
		void Invert();
	}
}

extern object Scn.Step.Chara.ObjColl
{
	void AddAttack(int, int, float);
	void AddAttack(int, int, float, float, float);
}

namespace Mint
{
	extern object Debug
	{
		void puts(string);
	}
}

object TestClass
{
	void Exec()
	{
		// this is a comment
		int x = 3; // comments like this work too
		int y = x + (3 + 2);
	
		HEL.Math.Vector3.SetX(0.5);

		HEL.Math.Vector3 vec = HEL.Math.Vector3;
		vec.x = 3.0;
		/*
		They also work like this.
		*/
		for (float f = 0.0; f < vec.x; ++f)
		{
			GObj.FootState.IsGround();
			for (int i = 0; i < 4; i += 1)
			{
				GObj.Target.Invert();
			}
		}

		Mint.Debug.puts("Hello Mint!");

		if (GetVec() >= 0.5)
		{
			int z = x * y;
		}
		else if (vec.x < 0.0)
		{
			int z = x / y;
		}
		else
		{
			return;
		}

		for (int i = 0; i < 7; i++)
		{
			Scn.Step.Chara.ObjColl.AddAttack(x - 2, i, 1.0);
			Scn.Step.Chara.ObjColl.AddAttack(1, 2, 3.0, 4.0, 5.0);
		}

		yield x + y;

		float count = 0.0;
		while (GObj.FootState.IsGround())
		{
			++count;
		}
	}

	float GetVec()
	{
		return HEL.Math.Vector3.GetX() + 0.5;
	}
}