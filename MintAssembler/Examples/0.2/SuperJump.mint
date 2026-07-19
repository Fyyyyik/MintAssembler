module Scn.Step.Hero.Interference.AnimScript.SuperJump;

include "SuperJump.hmint";

object SuperJump
{
	void __Init() { }

	void Exec()
	{
		if (Scn.Step.Hero.Utility.IsMeta())
			Scn.Step.Hero.SoundSE.Normal_Start(0xDE);
		else if (Scn.Step.Hero.Utility.IsDedede())
			Scn.Step.Hero.SoundSE.Normal_Start(0xE1);
		else if (Scn.Step.Hero.Utility.IsDee())
			Scn.Step.Hero.SoundSE.Normal_Start(0xE0);
		else
			Scn.Step.Hero.SoundSE.Normal_Start(0x10C);

		bool hasVacuumed = Scn.Step.Vacuum.Attacker.VacuumCount() > 0;
		if (hasVacuumed)
		{
			Scn.Step.Hero.ObjColl.SetBodyCollBig();
			GObj.MeshFlip.Flip(1);
			GObj.Anim.Start(0x28, false, 0.0);
		}
		else GObj.Anim.Start(8, false, 0.0);

		int attackType = 0;
		if (Scn.Step.Hero.Invincible.IsMighty())
			attackType = 0xE;
		else
			attackType = 0xD;
		Scn.Step.Chara.ObjColl.SetAttackType(0, attackType);
		Scn.Step.Chara.ObjColl.SetAttackCenter(0, 0.0, 0.0);
		Scn.Step.Chara.ObjColl.AddAttack(0, 0, 1.0, 0.0, 0.7);

		Scn.Step.Hero.Effect.BindState();
		Scn.Step.Chara.Effect.RequestN(0x4B3, 0);

		Scn.Step.Hero.Effect.BindState2();
		float angle = -45.0 * GObj.Target.GetSign();

		float zPos = 0.0;
		if (Scn.Step.Hero.Utility.IsDedede())
			zPos = 2.0;
		else
			zPos = 1.3;

		Scn.Step.Chara.Effect.RequestND(
			0x4B5,
			2,
			HEL.Math.Direction3(
				HEL.Math.Vector3(
					HEL.Math.Math.SinDegF(angle),
					HEL.Math.Math.CosDegF(angle),
					0.0
				),
				HEL.Math.Vector3(
					HEL.Math.Math.CosDegF(angle),
					HEL.Math.Math.SinDegF(angle),
					0.0
				),
				HEL.Math.Vector3(1.0, 0.0, 0.0)
			),
			HEL.Math.Vector3(0.0, 0.75, zPos)
		);

		angle = 360.0 * 5.0;
		int i = 0;
		while (true)
		{
			if (++i == 0x23)
			{
				Scn.Step.Hero.Effect.BindState();
				Scn.Step.Chara.Effect.Release();
			}

			if (i == 0x28)
			{
				Scn.Step.Hero.Effect.BindState2();
				Scn.Step.Chara.Effect.Release();
			}

			angle *= 0.925;
			float angleCpy = angle;

			while (angleCpy >= 360.0)
				angleCpy -= 360.0;

			Scn.Step.Chara.ModelRotCtrl.InitRotH(angleCpy + 50.0);

			if (hasVacuumed && Scn.Step.Vacuum.Attacker.VacuumCount() == 0)
				GObj.Anim.Start(8, false, 0.0);

			yield 1;
		}
	}
}