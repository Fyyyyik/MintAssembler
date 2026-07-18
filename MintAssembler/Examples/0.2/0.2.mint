module User.Tsuruoka.MintTest;

namespace HEL.Math
{
	mint object Vector3
	{
		float x;
		void SetX(float);
		float GetX();
	}

	extern object Direction3
	{
		
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
		bool IsAir();
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
		// hex values are like this
		const int VALUE = 0xC3;

		// Not allowed, will give an error when compiling
		//VALUE = 5;
		
		// this is a comment
		int x = 3; // comments like this work too
		int y = x + (3 + 2);
	
		HEL.Math.Vector3.SetX(0.5);

		ref HEL.Math.Vector3 vec = HEL.Math.Vector3;
		vec.x = 3.0;

		/*
		They also work
		like this.
		*/

		for (float f = 0.0; f < vec.x; ++f)
		{
			GObj.FootState.IsGround();
			for (int i = 0; i < 4; i += 1)
			{
				GObj.Target.Invert();
			}
			Scn.Step.Hero.Fire.AnimScript.Breath.SetAttack(1.0, 2.0, 3.0, 4.0, 5.0);
		}

		Mint.Debug.puts("Hello Mint!");

		if (GetVec() >= 0.5)
			int z = x * y; // if there's only one statement you can do this
		else if (vec.x < 0.0)
		{
			int z = x / y;
		}
		else return;

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

		do count--;
		while (GObj.FootState.IsAir());

		// Arrays
		int[] myArray1 = new int[] { 1, 2, 3, 4 };
		myArray1[1] = 5;

		float[] myArray2 = new float[3];
		myArray2[0] = 1.0;

		// ref are pointers
		ref int myPtr = 0x80001234; // a ram address
		const int myValue = *myPtr;
		(*myPtr)++;
		*myPtr = 3;

		ref float savedXPtr = vec->x;
		float savedX = *savedXPtr;

		ref float myPtr2 = 0x80005678;
	}

	float GetVec()
	{
		return HEL.Math.Vector3.GetX() + 0.5;
	}
}