namespace User.Tsuruoka;

xref HEL.Math.Vector3
{
	float x,
	void SetX(float),
	float GetX()
}

class TestClass
{
	void Exec()
	{
		int x = 3;
		int y = x + (3 + 2);
		
		HEL.Math.Vector3.SetX(0.5);

		if (this.GetVec() >= 0.5)
		{
			int z = x * y;
		}
		else if (HEL.Math.Vector3.x < 0.0)
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
		}

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

xref Scn.Step.Chara.ObjColl
{
	void AddAttack(int,int,float)
}

xref GObj.FootState
{
	bool IsGround()
}