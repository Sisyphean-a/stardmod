using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;

namespace HorseFollower;

internal sealed class HorseWalkAnimator
{
    private const float FrameDurationMilliseconds = 70f;
    private readonly List<FarmerSprite.AnimationFrame>[] animations =
    {
        new List<FarmerSprite.AnimationFrame>
        {
            new(15, (int)FrameDurationMilliseconds),
            new(16, (int)FrameDurationMilliseconds),
            new(17, (int)FrameDurationMilliseconds),
            new(18, (int)FrameDurationMilliseconds),
            new(19, (int)FrameDurationMilliseconds),
            new(20, (int)FrameDurationMilliseconds),
        },
        new List<FarmerSprite.AnimationFrame>
        {
            new(8, (int)FrameDurationMilliseconds),
            new(9, (int)FrameDurationMilliseconds),
            new(10, (int)FrameDurationMilliseconds),
            new(11, (int)FrameDurationMilliseconds),
            new(12, (int)FrameDurationMilliseconds),
            new(13, (int)FrameDurationMilliseconds),
        },
        new List<FarmerSprite.AnimationFrame>
        {
            new(1, (int)FrameDurationMilliseconds),
            new(2, (int)FrameDurationMilliseconds),
            new(3, (int)FrameDurationMilliseconds),
            new(4, (int)FrameDurationMilliseconds),
            new(5, (int)FrameDurationMilliseconds),
            new(6, (int)FrameDurationMilliseconds),
        },
        new List<FarmerSprite.AnimationFrame>
        {
            new(8, (int)FrameDurationMilliseconds, secondaryArm: false, flip: true),
            new(9, (int)FrameDurationMilliseconds, secondaryArm: false, flip: true),
            new(10, (int)FrameDurationMilliseconds, secondaryArm: false, flip: true),
            new(11, (int)FrameDurationMilliseconds, secondaryArm: false, flip: true),
            new(12, (int)FrameDurationMilliseconds, secondaryArm: false, flip: true),
            new(13, (int)FrameDurationMilliseconds, secondaryArm: false, flip: true),
        }
    };

    private int activeDirection = -1;
    private int frameIndex;
    private float frameTimer;
    private bool movedThisTick;

    // Rule: movement only records intent here; the final frame is applied after vanilla Horse.Update.
    internal void Animate(Horse horse, int direction)
    {
        SetDirection(direction);
        movedThisTick = true;
        Apply(horse);
    }

    internal void Maintain(Horse horse)
    {
        if (activeDirection == -1)
            SetDirection(horse.FacingDirection);

        Apply(horse);
    }

    // Guarantee: vanilla may stop or replace the sprite during the game update, so restore one authoritative frame afterward.
    internal void Tick(Horse horse, GameTime time)
    {
        if (activeDirection == -1)
            return;

        if (!movedThisTick)
        {
            ApplyIdle(horse);
            return;
        }

        frameTimer += Math.Max(0f, (float)time.ElapsedGameTime.TotalMilliseconds);
        while (frameTimer >= FrameDurationMilliseconds)
        {
            frameTimer -= FrameDurationMilliseconds;
            frameIndex = (frameIndex + 1) % animations[activeDirection].Count;
        }

        Apply(horse);
        movedThisTick = false;
    }

    internal void Reset()
    {
        activeDirection = -1;
        frameIndex = 0;
        frameTimer = 0f;
        movedThisTick = false;
    }

    private void SetDirection(int direction)
    {
        if (direction is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(direction));

        if (activeDirection == -1)
        {
            activeDirection = direction;
            frameIndex = 0;
            frameTimer = 0f;
        }
        else
        {
            // Rule: a turn changes only the directional frame group; gait phase is retained.
            activeDirection = direction;
        }
    }

    private void ApplyIdle(Horse horse)
    {
        horse.Sprite.CurrentAnimation = null;
        horse.Sprite.CurrentFrame = activeDirection switch
        {
            0 => 14,
            1 or 3 => 7,
            _ => 0
        };
        horse.Sprite.timer = 0f;
        horse.FacingDirection = activeDirection;
        horse.flip = activeDirection == 3;
        horse.drawOffset = activeDirection == 3 ? Vector2.Zero : new Vector2(-16f, 0f);
    }

    private void Apply(Horse horse)
    {
        List<FarmerSprite.AnimationFrame> animation = animations[activeDirection];
        if (!ReferenceEquals(horse.Sprite.CurrentAnimation, animation))
            horse.Sprite.setCurrentAnimation(animation);

        horse.Sprite.loop = true;
        horse.Sprite.currentAnimationIndex = frameIndex;
        horse.Sprite.CurrentFrame = animation[frameIndex].frame;
        horse.Sprite.timer = frameTimer;
        horse.FacingDirection = activeDirection;
        horse.flip = activeDirection == 3;
        horse.drawOffset = activeDirection == 3 ? Vector2.Zero : new Vector2(-16f, 0f);
    }
}
