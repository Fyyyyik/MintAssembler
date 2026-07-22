module Scn.Step.Hero.Fire.AnimScript.Breath;

namespace Scn.Step.Chara
{
	extern object ModelRotCtrl
	{
		void SetRotHTarget(float);
	}
	
	extern object Trigger
	{
		void Set(int);
	}
	
	extern object Effect
	{
		void RequestN(int, int);
	}
	
	extern object ObjColl
	{
		void SetAttackType(int, int);
		void SetAttackCenter(int, float, float);
		void AddAttack(int, int, float, float, float, float, float);
		void ClearAttack();
	}
}

namespace GObj
{
	extern object Anim
	{
		void Start(int, bool, float);
		void SetFrameRate(float);
	}

	extern object MeshFlip
	{
		void Flip(int);
	}
	
	extern object Model
	{
		void LoadNode(int);
	}
}

namespace Scn.Step.Hero
{
	extern object Effect
	{
		void BindState();
	}
	
	extern object Utility
	{
		int PlayerCount();
		bool IsSeparateProcessMyTurn();
	}
}

namespace G3D
{
	extern object NodeAccessor
	{
		void LoadWorldRotate();
		void LoadWorldScale();
	}
}

namespace HEL.Math
{
	extern object Direction3
	{
		void StoreFront();
	}
	
	extern object Vector3
	{
		float GetX();
		float GetY();
		float GetZ();
	}
}

object Breath
{
	void __Init() { }
	
	void Exec()
	{
		Scn.Step.Chara.ModelRotCtrl.SetRotHTarget(60.0);
		GObj.Anim.Start(200, false, 0.0);
		GObj.Anim.SetFrameRate(1.25);
		GObj.Anim.WaitTillIsAnimEnd();
		GObj.MeshFlip.Flip(3);

		GObj.Anim.Start(201, true, 0.0);
		Scn.Step.Chara.Trigger.Set(0);
		Scn.Step.Hero.Effect.BindState();
		
		int effect;
		if (Scn.Step.Hero.Utility.PlayerCount() == 4)
			effect = 364;
		else
			effect = 363;
		Scn.Step.Chara.Effect.RequestN(effect, 7);
		
		Scn.Step.Hero.SoundSE.Loop_Start(407);
		
		Scn.Step.Chara.ObjColl.SetAttackType(0, 74);
		Scn.Step.Chara.ObjColl.SetAttackCenter(1, 0.0, 0.0);
		
		SetAttack(0.3, 1.0, 0.41, 1.0, 0.61);
		yield 1;

		SetAttack(0.3, 1.5, 0.4, 1.5, 0.71);
		yield 1;
		
		SetAttack(0.35, 2.0, 0.44, 2.0, 1.0);
		yield 1;
		
		SetAttack(0.4, 2.6, 0.4, 2.6, 1.5);
		yield 1;
		
		SetAttack(0.4, 3.25, 0.3, 3.25, 1.7);
		yield 2;
		
		while (true)
		{
			while (!Scn.Step.Hero.Utility.IsSeparateProcessMyTurn())
				yield 1;
			
			Scn.Step.Chara.ObjColl.ClearAttack();
			SetAttack(0.3, 1.0, 0.41, 1.0, 0.61);
			SetAttack(0.3, 1.5, 0.4, 1.5, 0.71);
			SetAttack(0.35, 2.0, 0.44, 2.0, 1.0);
			SetAttack(0.4, 2.7, 0.4, 2.7, 1.5);
			SetAttack(0.45, 3.6, 0.3, 3.6, 1.7);
			
			yield 1;
		}
	}
	
	void SetAttack(
		float radius,
		float xMul1,
		float yMul1,
		float xMul2,
		float yMul2)
	{
		GObj.Model.LoadNode(7);
		G3D.NodeAccessor.LoadWorldRotate();
		HEL.Math.Direction3.StoreFront();
		
		float x1 = HEL.Math.Vector3.GetX() * xMul1;
		float y1 = HEL.Math.Vector3.GetY() * yMul1;
		
		float x2 = HEL.Math.Vector3.GetX() * xMul2;
		float y2 = HEL.Math.Vector3.GetY() * yMul2;
		
		G3D.NodeAccessor.LoadWorldScale();
		
		x1 *= HEL.Math.Vector3.GetZ();
		y1 *= HEL.Math.Vector3.GetZ();
		x2 *= HEL.Math.Vector3.GetZ();
		y2 *= HEL.Math.Vector3.GetZ();
		
		y1 += yMul1;
		y2 += yMul2;
		
		Scn.Step.Chara.ObjColl.AddAttack(0, 0, radius, x1, y1, x2, y2);
	}
}