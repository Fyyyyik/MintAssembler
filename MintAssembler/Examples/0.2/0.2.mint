module User.Tsuruoka.MintTest;

// <> tells it to search based on the path of the currently running MintAssembler executable, without those it checks based on the path of the current module
include <"Examples/0.2/0.2.hmint">; // use slashes '/' to separate directory levels

namespace HEL.Math
{
	mint object Vector3
	{
		float x;
		void SetX(float);
		float GetX();

		this(float, float, float);
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
	int intVal1;
	int intVal2 = 3;

	const ref HEL.Math.Vector3 myVec3 = HEL.Math.Vector3(1.0, 2.0, 3.0);

	float[] myValues = { 1.0, 2.0, 3.0 };
	
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
			
			if (i == 5) break;
		}

		yield x + y;

		float count = 0.0;
		while (GObj.FootState.IsGround())
		{
			break;
			++count;
		}

		do count--;
		while (GObj.FootState.IsAir());

		// Arrays
		int[] myArray1 = { 1, 2, 3, 4 };
		myArray1[1] = 5;
		int[3] myArray2; // creates an array with 3 elements, all set to 0
		float[5] myArray3 = { 1.0, 2.0, 3.0, 4.0, 5.0 };

		float[2] myArray4;
		myArray4[0] = 1.0;

		// ref are pointers
		ref int myPtr = 0x80001234; // a ram address
		const int myValue = *myPtr;
		(*myPtr)++;
		*myPtr = 3;

		ref float savedXPtr = vec->x;
		float savedX = *savedXPtr;

		ref float myPtr2 = 0x80005678;

		int castedInt = (int)*myPtr2;
		float castedFloat = (float)savedX;

		// doesn't work : only reference types can be dereferenced. Instead load pointer as a reference in a ref variable then dereference that
		//int staticRamValue = *0x80001234;

		if (x == y && x == 3)
			yield GObj.FootState.IsGround() ? 1 : x; // ternary operator

		switch (x)
		{
			case 1:
				Mint.Debug.puts("x is 1");
				break;
			case 2:
				for (int j = 0; j < 6; j++)
					Mint.Debug.puts("x is 2");
				x += 2;
				break;
			case 3, 4, 6:
				Mint.Debug.puts("x is 3, 4 or 6");
				break;
			default:
				Mint.Debug.puts("the default case, if no other case matches");
				break;
			case 5:
				Mint.Debug.puts("ahbgqiujfbckqiebvc");
				break;
		}

		string message = x switch
		{
			1 => "Expr switch : x is 1",
			2 => "Expr switch : x is 2",
			3 => "Expr switch : x is 3",
			4, 5, 6 => "Expr switch : x is 4, 5 or 6",

			_ => "idk what x is lol"
		};

		int wtf = {1, 2, 3, 4}[2];
	}

	float GetVec()
	{
		return HEL.Math.Vector3.GetX() + 0.5;
	}
}