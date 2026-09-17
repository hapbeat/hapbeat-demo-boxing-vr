using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingAvatarTests
    {
        private BoxingGame game;
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>(); game.Initialize();
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.menu.Close();
        }
        [TearDown] public void Cleanup() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        [TestCase(1.25f)] [TestCase(1.65f)] [TestCase(1.95f)]
        public void SkinnedFistsAndHeadFollowCollisionTargetsThroughAttacksAndGuards(float height)
        {
            var avatar = game.presentation.enemyAvatar;
            Assert.That(avatar.skin.sharedMesh.vertexCount, Is.GreaterThan(1000));
            Assert.That(avatar.bones.Length, Is.EqualTo(20));
            Assert.That(GameObject.Find("Opponent - sparring partner"), Is.Null);
            game.Opponent.Reset(height); bool attack = false, guard = false;
            for (int i=0; i<1800; i++)
            {
                game.Opponent.Tick(1f/90,new Vector3(0,height,.2f),true);
                avatar.Render(game.Opponent,1f/90);
                Assert.That(Vector3.Distance(avatar.LeftCenter,game.Opponent.Left),Is.LessThan(.002f));
                Assert.That(Vector3.Distance(avatar.RightCenter,game.Opponent.Right),Is.LessThan(.002f));
                Assert.That(Vector3.Distance(avatar.HeadCenter,game.Opponent.Head),Is.LessThan(.002f));
                attack |= game.Opponent.Striking; guard |= game.Opponent.Guarding;
            }
            Assert.That(attack && guard,Is.True);
            Assert.That(game.feedback.Sends,Is.Zero);
        }
        [Test] public void HeadAndBodyReactionsDifferRecoverAndDoNotMoveHitTargets()
        {
            var avatar=game.presentation.enemyAvatar; var target=game.Opponent.Head;
            avatar.React(false,1); avatar.Render(game.Opponent,0); var headRotation=avatar.chest.rotation;
            avatar.ResetReaction(); avatar.React(true,1); avatar.Render(game.Opponent,0);
            Assert.That(Quaternion.Angle(headRotation,avatar.chest.rotation),Is.GreaterThan(10));
            Assert.That(Vector3.Distance(target,avatar.HeadCenter),Is.LessThan(.002f));
            Assert.That(game.Opponent.Head,Is.EqualTo(target));
            for(int i=0;i<60;i++) avatar.Render(game.Opponent,1f/90);
            Assert.That(avatar.Reaction,Is.Zero);
            avatar.React(true,1); game.StartRound(); Assert.That(avatar.Reaction,Is.Zero);
        }
        [TestCase(true)] [TestCase(false)]
        public void RealContactSelectsHeadOrBodyReaction(bool head)
        {
            game.StartRound(); game.Round.Tick(3,false);
            var pose=BoxingSceneTests.Pose(); var target=head ? game.Opponent.Head : game.Opponent.Body;
            pose.left=target+Vector3.right*.45f;
            game.input.SetTestPose(pose);
            for(int i=0;i<30;i++) game.Simulate(1f/90,pose);
            pose.left=head ? game.Opponent.Head : game.Opponent.Body;
            game.input.SetTestPose(pose); game.Simulate(1f/90,pose);
            Assert.That(game.Round.Hits,Is.EqualTo(1));
            Assert.That(game.presentation.enemyAvatar.Reaction,Is.GreaterThan(0));
            Assert.That(game.presentation.enemyAvatar.BodyReaction,Is.EqualTo(!head));
            Assert.That(game.feedback.Sends,Is.Zero);
        }
    }
}
